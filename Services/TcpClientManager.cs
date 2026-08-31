using SocketTestTool.Common;
using SocketTestTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace SocketTestTool.Services
{
    /// <summary>
    /// 단일 TCP 클라이언트 연결을 관리하는 서비스 클래스입니다.
    /// 비동기(async/await) 방식으로 서버에 접속하고 데이터를 송수신합니다.
    /// </summary>
    public class TcpClientManager
    {
        #region Fields

        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cancellationTokenSource;
        private System.Timers.Timer? _periodicTimer;

        // 서버와 같은 값입니다. 쉬지 않고 보내는 상대가 한 건을 무한히 키워
        // 메모리를 고갈시키는 것을 막습니다. (QA-HISTORY.md 결함 #11)
        private const int MaxAccumulatedBytes = 16 * 1024 * 1024;

        #endregion

        #region Events & Properties

        /// <summary>
        /// 로그 항목이 발생했을 때 ViewModel에 알리기 위한 이벤트입니다.
        /// </summary>
        public event Action<LogEntry>? LogEntryReceived;

        /// <summary>
        /// 연결 상태가 변경되었을 때 ViewModel에 알리기 위한 이벤트입니다.
        /// </summary>
        public event Action<string>? StatusChanged;

        /// <summary>
        /// 접속에 실패했거나 연결이 예기치 않게 끊겼을 때 발생합니다.
        /// (사용자에게 보여 줄 문구, 접속 실패인지 여부)
        /// </summary>
        public event Action<string, bool>? ConnectionFailed;

        /// <summary>
        /// 현재 서버에 연결되어 있는지 여부를 나타냅니다.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// 이 클라이언트가 사용할 인코딩 방식입니다. ViewModel에서 설정합니다.
        /// </summary>
        public Encoding CurrentEncoding { get; set; } = Encoding.ASCII;

        /// <summary>
        /// 실시간 로그 파일 저장 기능 활성화 여부입니다. ViewModel에서 설정합니다.
        /// </summary>
        public bool IsRealtimeLogEnabled { get; set; }

        /// <summary>
        /// 상대가 보내온 데이터에 자동으로 응답할지를 정합니다.
        /// "ReplyAfterReceive"면 <see cref="ReplyMessage"/>를 회신하고, 그 밖(기본 "ListenOnly")이면
        /// 자동 응답하지 않습니다. 서버와 같은 값을 씁니다.
        /// </summary>
        public string ResponsePattern { get; set; } = "ListenOnly";

        /// <summary>
        /// 받은 내용에 따라 다르게 회신하는 규칙입니다. 자동 응답 방식보다 우선합니다.
        /// <see cref="ResponsePattern"/>이 "ListenOnly"여도 규칙은 동작합니다.
        /// </summary>
        public List<ResponseRule> Rules { get; set; } = new List<ResponseRule>();

        /// <summary>
        /// "ReplyAfterReceive"일 때 회신할 내용입니다. [STX] 같은 제어문자 태그를 쓸 수 있습니다.
        /// </summary>
        public string? ReplyMessage { get; set; }

        /// <summary>
        /// 자동 응답을 받을 때마다 계속할지(true), 처음 한 번만 할지(false)를 정합니다.
        /// </summary>
        public bool IsReplyEndless { get; set; }

        /// <summary>
        /// 수신 조각을 합치기 위해 기다리는 침묵 시간(ms)입니다.
        /// <b>0이면 합치지 않고 받는 대로 처리합니다.</b> 서버의 같은 이름 설정과 동작이 같습니다.
        /// </summary>
        public int ReceiveTimeout { get; set; }

        #endregion

        #region Public Methods

        /// <summary>
        /// 지정된 IP 주소와 포트로 서버에 비동기 접속을 시도합니다.
        /// </summary>
        public async Task Connect(string ipAddress, int port)
        {
            if (IsConnected) return;

            _client = new TcpClient();
            _cancellationTokenSource = new CancellationTokenSource();
            try
            {
                await _client.ConnectAsync(ipAddress, port).ConfigureAwait(false);
                IsConnected = true;
                _stream = _client.GetStream();
                StatusChanged?.Invoke("Connected");
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = $"Connected to server at {ipAddress}:{port}" });

                // 데이터 수신을 위한 별도의 백그라운드 작업을 시작합니다.
                _ = ReceiveDataAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = $"Connection Error: {ex.Message}" });
                ConnectionFailed?.Invoke(ex.Message, true);
                Disconnect();
            }
        }

        /// <summary>
        /// 현재 연결을 안전하게 종료하고 관련 리소스를 모두 해제합니다.
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected && _client == null) return;

            StopPeriodicSend();
            _cancellationTokenSource?.Cancel(); // 모든 비동기 작업(특히 ReceiveDataAsync)에 취소 신호를 보냅니다.
            _stream?.Close();
            _client?.Close();
            _client = null;
            IsConnected = false;
            StatusChanged?.Invoke("Stopped");
            LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "Disconnected from server." });
        }

        /// <summary>
        /// 서버에 문자열 데이터를 비동기적으로 전송합니다.
        /// </summary>
        public async Task Send(string message, Encoding encoding)
        {
            if (!IsConnected)
            {
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "Not connected to server." });
                return;
            }
            try
            {
                var stream = _stream;
                if (stream == null) return; // Disconnect()와 겹친 경우

                string parsedMessage = AsciiTagParser.Parse(message);
                byte[] data = encoding.GetBytes(parsedMessage);
                await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.Sent, Message = "", Data = data, Length = data.Length });
            }
            catch (Exception ex)
            {
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = $"Send Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// 주기적 전송을 시작합니다.
        /// </summary>
        public void StartPeriodicSend(string message, int interval, Encoding encoding)
        {
            if (_periodicTimer != null) StopPeriodicSend();
            _periodicTimer = new System.Timers.Timer(interval);
            _periodicTimer.Elapsed += async (sender, e) => await Send(message, encoding);
            _periodicTimer.AutoReset = true;
            _periodicTimer.Enabled = true;
        }

        /// <summary>
        /// 주기적 전송을 중지합니다.
        /// </summary>
        public void StopPeriodicSend()
        {
            if (_periodicTimer != null)
            {
                _periodicTimer.Enabled = false;
                _periodicTimer.Dispose();
                _periodicTimer = null;
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "Periodic sending stopped." });
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 백그라운드에서 서버로부터 들어오는 데이터를 지속적으로 수신 대기하는 메서드입니다.
        /// </summary>
        /// <summary>
        /// 받은 데이터에 대해 설정된 자동 응답을 보냅니다.
        /// 서버(TcpServerManager)와 같은 우선순위입니다: 규칙 기반이 먼저, 그 다음 고정 응답.
        /// </summary>
        /// <param name="hasRepliedOnce">지금까지 한 번이라도 고정 응답을 보냈는지 여부입니다.</param>
        /// <returns>이번 호출까지 포함해 고정 응답을 보낸 적이 있는지 여부입니다.</returns>
        private async Task<bool> ReplyIfConfiguredAsync(NetworkStream stream, byte[] received,
                                                        bool hasRepliedOnce, CancellationToken token)
        {
            // [우선순위 1] 규칙 기반 응답 — ResponsePattern과 무관하게 동작합니다.
            if (Rules != null && Rules.Count > 0)
            {
                string receivedText = CurrentEncoding.GetString(received);
                var matched = Rules.FirstOrDefault(r => !string.IsNullOrEmpty(r.ReceiveData) &&
                                                        receivedText.Contains(r.ReceiveData));
                if (matched != null)
                {
                    string parsed = AsciiTagParser.Parse(matched.SendData ?? string.Empty);
                    byte[] bytes = CurrentEncoding.GetBytes(parsed);
                    await stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                    LogEntryReceived?.Invoke(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Direction = LogDirection.Sent,
                        Message = "Rule",
                        Data = bytes,
                        Length = bytes.Length
                    });
                    return hasRepliedOnce; // 규칙 응답은 '1회 응답' 횟수에 넣지 않습니다.
                }
            }

            // [우선순위 2] 고정 응답
            if (ResponsePattern != "ReplyAfterReceive") return hasRepliedOnce;
            if (hasRepliedOnce && !IsReplyEndless) return hasRepliedOnce;
            if (string.IsNullOrEmpty(ReplyMessage)) return true;

            string parsedReply = AsciiTagParser.Parse(ReplyMessage);
            byte[] replyBytes = CurrentEncoding.GetBytes(parsedReply);
            await stream.WriteAsync(replyBytes, 0, replyBytes.Length, token).ConfigureAwait(false);
            LogEntryReceived?.Invoke(new LogEntry
            {
                Timestamp = DateTime.Now,
                Direction = LogDirection.Sent,
                Message = "Auto reply",
                Data = replyBytes,
                Length = replyBytes.Length
            });

            return true;
        }

        private async Task ReceiveDataAsync(CancellationToken token)
        {
            try
            {
                var stream = _stream;
                if (stream == null) return;

                byte[] buffer = new byte[4096]; // 4KB 버퍼
                var accumulated = new List<byte>();
                bool hasRepliedOnce = false;

                while (!token.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        // 서버가 정상적으로 연결을 종료한 경우
                        LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "Server closed the connection." });
                        ConnectionFailed?.Invoke("Peer closed the connection.", false);
                        Disconnect();
                        break;
                    }

                    // Take(n).ToArray()는 바이트 단위로 열거합니다. 스팬 복사가 훨씬 빠릅니다.
                    accumulated.AddRange(buffer.AsSpan(0, bytesRead).ToArray());

                    // ReceiveTimeout 동안 추가 데이터가 더 오는지 지켜보다가 한 건으로 합칩니다.
                    // 0이면 지금까지처럼 받는 대로 곧바로 처리합니다.
                    // 상한(MaxAccumulatedBytes)은 쉬지 않고 보내는 상대가 메모리를 고갈시키는 것을 막습니다.
                    if (ReceiveTimeout > 0)
                    {
                        var lastDataTime = DateTime.Now;
                        while ((DateTime.Now - lastDataTime).TotalMilliseconds < ReceiveTimeout
                               && accumulated.Count < MaxAccumulatedBytes)
                        {
                            if (stream.DataAvailable)
                            {
                                int more = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                                if (more == 0) break;

                                accumulated.AddRange(buffer.AsSpan(0, more).ToArray());
                                lastDataTime = DateTime.Now;
                            }
                            else
                            {
                                await Task.Delay(10, token).ConfigureAwait(false);
                            }
                        }
                    }

                    byte[] finalData = accumulated.ToArray();
                    accumulated.Clear();

                    LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.Received, Message = "", Data = finalData, Length = finalData.Length });

                    hasRepliedOnce = await ReplyIfConfiguredAsync(stream, finalData, hasRepliedOnce, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Disconnect() 호출 시 CancellationTokenSource가 Cancel되면서 정상적으로 발생하는 예외
            }
            catch (Exception)
            {
                // 네트워크 케이블 분리 등 비정상적인 연결 끊김 발생
                if (IsConnected)
                {
                    LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "Connection lost." });
                    ConnectionFailed?.Invoke("Connection lost.", false);
                    Disconnect();
                }
            }
        }

        #endregion
    }
}