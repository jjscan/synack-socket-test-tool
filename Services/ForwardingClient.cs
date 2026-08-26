using SocketTestTool.Models;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SocketTestTool.Services
{
    /// <summary>
    /// 한 연결에서 수신한 원본 바이트를 다른 소켓 서버로 자동 전달(포워딩)하는 서비스 클래스입니다.
    /// 대상 서버가 죽어 있거나 도중에 끊겨도 데이터를 잃지 않도록,
    /// 내부 큐에 보관해 두었다가 재접속에 성공하면 순서대로 다시 전송합니다.
    /// </summary>
    public class ForwardingClient
    {
        #region Constants

        /// <summary>
        /// 대상 서버가 끊겨 있는 동안 보관할 수 있는 최대 메시지 개수입니다.
        /// 메모리가 무한정 늘어나는 것을 막기 위한 상한이며, 넘어가면 가장 오래된 것부터 버립니다.
        /// </summary>
        private const int MaxQueuedMessages = 1000;

        /// <summary>
        /// 재접속을 다시 시도하기까지 기다리는 시간(ms)입니다.
        /// </summary>
        private const int ReconnectDelayMs = 3000;

        #endregion

        #region Fields

        private readonly string _targetIp;
        private readonly int _targetPort;

        // 전송 대기 중인 데이터입니다. _queue 자체를 잠금 객체로 사용합니다.
        private readonly Queue<byte[]> _queue = new Queue<byte[]>();

        private CancellationTokenSource _cancellationTokenSource;
        private TcpClient _client;
        private bool _isRunning;

        // 큐가 가득 차서 버린 메시지 개수입니다. 로그를 도배하지 않도록 모아서 알립니다.
        private int _droppedCount;

        #endregion

        #region Events & Properties

        /// <summary>
        /// 전달 상태(접속/끊김/유실 등)를 ViewModel의 로그로 올리기 위한 이벤트입니다.
        /// </summary>
        public event Action<LogEntry> LogEntryReceived;

        /// <summary>
        /// 현재 대상 서버에 접속되어 있는지 여부입니다.
        /// </summary>
        public bool IsConnected { get; private set; }

        #endregion

        #region Constructor

        public ForwardingClient(string targetIp, int targetPort)
        {
            _targetIp = targetIp;
            _targetPort = targetPort;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 전달을 시작합니다. 접속과 전송은 모두 백그라운드에서 처리되므로 즉시 반환됩니다.
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            Log($"Forwarding enabled -> {_targetIp}:{_targetPort}");

            _ = Task.Run(() => PumpAsync(_cancellationTokenSource.Token));
        }

        /// <summary>
        /// 전달을 중지하고 대상 서버와의 연결 및 대기 중인 데이터를 정리합니다.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationTokenSource?.Cancel();
            CloseClient();

            int discarded;
            lock (_queue)
            {
                discarded = _queue.Count;
                _queue.Clear();
            }

            string tail = discarded > 0 ? $" ({discarded} message(s) not forwarded)" : "";
            Log($"Forwarding stopped.{tail}");
        }

        /// <summary>
        /// 전달할 데이터를 큐에 넣습니다. 소켓 수신 스레드에서 호출되므로 절대 블로킹하지 않습니다.
        /// </summary>
        /// <param name="data">수신한 원본 바이트 배열입니다.</param>
        public void Enqueue(byte[] data)
        {
            if (!_isRunning || data == null || data.Length == 0) return;

            int dropped = 0;
            lock (_queue)
            {
                // 상한을 넘으면 가장 오래된 데이터부터 버립니다.
                while (_queue.Count >= MaxQueuedMessages)
                {
                    _queue.Dequeue();
                    dropped++;
                }
                _queue.Enqueue(data);
            }

            if (dropped > 0)
            {
                // 100건 단위로만 알려서 로그가 폭주하지 않게 합니다.
                int total = Interlocked.Add(ref _droppedCount, dropped);
                if (total % 100 < dropped)
                {
                    Log($"Forwarding buffer is full. {total} message(s) dropped so far.");
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 백그라운드에서 접속 유지와 큐 비우기를 반복하는 메인 루프입니다.
        /// </summary>
        private async Task PumpAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!IsConnected)
                    {
                        bool connected = await TryConnectAsync(token).ConfigureAwait(false);
                        if (!connected)
                        {
                            // 접속 실패 시 잠시 쉬었다가 다시 시도합니다. 그동안 데이터는 큐에 쌓입니다.
                            await Task.Delay(ReconnectDelayMs, token).ConfigureAwait(false);
                            continue;
                        }
                    }

                    // 상대가 이미 연결을 닫았는지 먼저 확인합니다.
                    // TCP 특성상 상대가 사라진 직후의 첫 쓰기는 OS 송신 버퍼에 담기며 '성공'으로 반환되기 때문에,
                    // 이 검사를 생략하면 끊기는 순간의 1건이 보내진 것으로 오인되어 큐에서 사라집니다.
                    if (IsPeerClosed())
                    {
                        CloseClient();
                        Log($"Forwarding target {_targetIp}:{_targetPort} closed the connection. Reconnecting...");
                        await Task.Delay(ReconnectDelayMs, token).ConfigureAwait(false);
                        continue;
                    }

                    byte[] data = Peek();
                    if (data == null)
                    {
                        // 보낼 것이 없으면 짧게 쉽니다.
                        await Task.Delay(20, token).ConfigureAwait(false);
                        continue;
                    }

                    var stream = _client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false);

                    // 전송에 성공한 뒤에야 큐에서 실제로 빼냅니다.
                    // 이렇게 해야 전송 도중 끊겼을 때 해당 데이터를 잃지 않고 재전송할 수 있습니다.
                    Dequeue();

                    LogEntryReceived?.Invoke(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Direction = LogDirection.Sent,
                        Message = $"Forwarded to {_targetIp}:{_targetPort}",
                        Data = data,
                        Length = data.Length
                    });
                }
                catch (OperationCanceledException)
                {
                    // Stop() 호출에 의한 정상 종료입니다.
                    break;
                }
                catch (Exception ex)
                {
                    // 전송 실패: 연결을 끊고 다음 루프에서 재접속합니다. 데이터는 큐에 그대로 남아 있습니다.
                    CloseClient();
                    Log($"Forwarding send failed: {ex.Message}. Reconnecting...");

                    try { await Task.Delay(ReconnectDelayMs, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        /// <summary>
        /// 대상 서버에 접속을 시도합니다.
        /// </summary>
        private async Task<bool> TryConnectAsync(CancellationToken token)
        {
            try
            {
                var client = new TcpClient();
                await client.ConnectAsync(_targetIp, _targetPort).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    client.Close();
                    return false;
                }

                _client = client;
                IsConnected = true;

                int pending = Count();
                string tail = pending > 0 ? $" Flushing {pending} buffered message(s)." : "";
                Log($"Forwarding connected to {_targetIp}:{_targetPort}.{tail}");
                return true;
            }
            catch (Exception)
            {
                // 대상 서버가 아직 안 떠 있는 경우가 대부분이므로 매번 로그를 남기지는 않습니다.
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// 대상 서버가 연결을 닫았는지 확인합니다.
        /// 읽기 가능 상태인데 읽을 수 있는 바이트가 0이면 상대가 연결을 종료한 것입니다.
        /// 이 방식으로도 '검사 직후에 상대가 죽는' 찰나의 경우까지는 막을 수 없지만,
        /// 일반적인 서버 종료/재기동 상황에서의 데이터 유실은 방지됩니다.
        /// </summary>
        private bool IsPeerClosed()
        {
            try
            {
                var socket = _client?.Client;
                if (socket == null) return true;

                if (socket.Available > 0)
                {
                    // 대상 서버가 응답을 보내온 경우입니다. 전달 전용 연결이라 내용은 쓰지 않지만,
                    // 그대로 두면 수신 버퍼가 차서 상대 쪽 전송이 막히므로 비워냅니다.
                    var discard = new byte[socket.Available];
                    socket.Receive(discard);
                    return false;
                }

                return socket.Poll(0, SelectMode.SelectRead);
            }
            catch (Exception)
            {
                return true;
            }
        }

        private void CloseClient()
        {
            IsConnected = false;
            try { _client?.Close(); } catch (Exception) { /* 정리 중 발생하는 예외는 무시합니다. */ }
            _client = null;
        }

        private byte[] Peek()
        {
            lock (_queue) { return _queue.Count > 0 ? _queue.Peek() : null; }
        }

        private void Dequeue()
        {
            lock (_queue) { if (_queue.Count > 0) _queue.Dequeue(); }
        }

        private int Count()
        {
            lock (_queue) { return _queue.Count; }
        }

        private void Log(string message)
        {
            LogEntryReceived?.Invoke(new LogEntry
            {
                Timestamp = DateTime.Now,
                Direction = LogDirection.System,
                Message = message
            });
        }

        #endregion
    }
}
