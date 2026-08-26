using SocketTestTool.Common;
using SocketTestTool.Models;
using System;
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

        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cancellationTokenSource;
        private System.Timers.Timer _periodicTimer;

        #endregion

        #region Events & Properties

        /// <summary>
        /// 로그 항목이 발생했을 때 ViewModel에 알리기 위한 이벤트입니다.
        /// </summary>
        public event Action<LogEntry> LogEntryReceived;

        /// <summary>
        /// 연결 상태가 변경되었을 때 ViewModel에 알리기 위한 이벤트입니다.
        /// </summary>
        public event Action<string> StatusChanged;

        /// <summary>
        /// 접속에 실패했거나 연결이 예기치 않게 끊겼을 때 발생합니다.
        /// (사용자에게 보여 줄 문구, 접속 실패인지 여부)
        /// </summary>
        public event Action<string, bool> ConnectionFailed;

        /// <summary>
        /// 현재 서버에 연결되어 있는지 여부를 나타냅니다.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// 이 클라이언트가 사용할 인코딩 방식입니다. ViewModel에서 설정합니다.
        /// </summary>
        public Encoding CurrentEncoding { get; set; }

        /// <summary>
        /// 실시간 로그 파일 저장 기능 활성화 여부입니다. ViewModel에서 설정합니다.
        /// </summary>
        public bool IsRealtimeLogEnabled { get; set; }

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
                string parsedMessage = AsciiTagParser.Parse(message);
                byte[] data = encoding.GetBytes(parsedMessage);
                await _stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
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
        private async Task ReceiveDataAsync(CancellationToken token)
        {
            try
            {
                byte[] buffer = new byte[4096]; // 4KB 버퍼
                while (!token.IsCancellationRequested)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        // 서버가 정상적으로 연결을 종료한 경우
                        LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "Server closed the connection." });
                        ConnectionFailed?.Invoke("Peer closed the connection.", false);
                        Disconnect();
                        break;
                    }
                    // Take(n).ToArray()는 바이트 단위로 열거합니다. 스팬 복사가 훨씬 빠릅니다.
                    var receivedBytes = buffer.AsSpan(0, bytesRead).ToArray();
                    LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.Received, Message = "", Data = receivedBytes, Length = bytesRead });
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