using SocketTestTool.Common;
using SocketTestTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace SocketTestTool.Services
{
    /// <summary>
    /// 단일 TCP 서버(Listener)를 관리하는 서비스 클래스입니다.
    /// 여러 클라이언트의 접속을 비동기적으로 수락하고, 각 클라이언트에 대한 데이터 처리를 담당합니다.
    /// </summary>
    public class TcpServerManager
    {
        #region Fields

        private TcpListener? _listener;
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private CancellationTokenSource? _cancellationTokenSource;
        private System.Timers.Timer? _periodicTimer;

        // Stop()이 시작됐음을 알리는 플래그입니다.
        // 리스너를 닫으면 대기 중이던 Accept가 예외로 깨어나는데,
        // 그 예외를 '서버를 열지 못했다'는 오류로 오해하지 않기 위해 필요합니다.
        private volatile bool _isStopping;

        // [보안] 한 프레임으로 누적할 수 있는 최대 바이트 수입니다. (CWE-400 자원 고갈 방지)
        //
        // 수신 누적은 ReceiveTimeout(기본 300ms)의 '침묵'이 올 때까지 계속됩니다.
        // 이 상한이 없으면, 악의적 클라이언트가 침묵 없이 계속 스트리밍할 때
        // accumulatedData가 무한히 커져 단일 연결만으로 프로세스를 OutOfMemory로 죽일 수 있습니다.
        // 이 값(16MB)은 이 도구가 다룰 법한 어떤 정상 전문보다도 훨씬 크므로 정상 사용에는 영향이 없고,
        // 상한에 닿으면 거기까지를 한 프레임으로 처리한 뒤 계속 수신합니다.
        private const int MaxAccumulatedBytes = 16 * 1024 * 1024;

        // [보안] 서버 하나가 동시에 받아들이는 최대 클라이언트 수입니다. (CWE-410 방지)
        // 이 도구의 정상 사용(장비 몇 대를 붙여 보는 것)보다 훨씬 넉넉하며,
        // 접속만 반복하는 공격이 메모리·스레드풀을 고갈시키지 못하게 막습니다.
        private const int MaxConcurrentClients = 512;

        // 수신 대기 타임아웃 (기본값 300ms, 데이터가 끊겨 들어올 때 기다리는 시간)
        public int ReceiveTimeout { get; set; } = 300;

        #endregion

        #region Events & Properties

        public event Action<LogEntry>? LogEntryReceived;
        public event Action<string>? StatusChanged;

        /// <summary>
        /// 서버를 열지 못했을 때 발생합니다. (사용자에게 보여 줄 문구, 개발자용 원인, 소켓 오류 코드)
        /// 서비스는 UI를 직접 띄우지 않고 이 이벤트만 올립니다. 화면 표시는 ViewModel이 결정합니다.
        /// </summary>
        public event Action<string, string, SocketError>? StartFailed;

        public bool IsRunning { get; private set; }
        public string ResponsePattern { get; set; } = "Echo";
        public List<ResponseRule> Rules { get; set; } = new List<ResponseRule>();
        public string? ReplyMessage { get; set; }
        public bool IsReplyEndless { get; set; }
        public Encoding CurrentEncoding { get; set; } = Encoding.UTF8;
        public bool IsRealtimeLogEnabled { get; set; }

        #endregion

        #region Public Methods

        /// <summary>
        /// 지정된 IP 주소와 포트로 서버 리스닝을 비동기적으로 시작합니다.
        /// </summary>
        public async Task Start(string ipAddress, int port)
        {
            if (IsRunning) return;

            _isStopping = false;
            _cancellationTokenSource = new CancellationTokenSource();
            try
            {
                _listener = new TcpListener(IPAddress.Parse(ipAddress), port);
                _listener.Start();
                IsRunning = true;
                StatusChanged?.Invoke("Listening");
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = $"Server started at {ipAddress}:{port}" });

                // 취소 요청이 들어올 때까지 계속해서 클라이언트 연결을 수락합니다.
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (client == null) continue; // 실제로는 발생하지 않지만, 이후 코드를 단순하게 유지합니다.

                    string clientIp = (client.Client?.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "Unknown IP";

                    // [보안] 동시 접속 수를 제한합니다. (CWE-410 연결 폭주 방지)
                    // 접속마다 8KB 읽기 버퍼와 처리 작업이 붙으므로, 무제한으로 받으면
                    // 접속만 반복해도 메모리·스레드풀을 고갈시킬 수 있습니다.
                    // 상한을 넘으면 그 접속만 즉시 끊고 계속 수락을 이어갑니다(서버는 살아 있음).
                    bool rejected = false;
                    lock (_clients)
                    {
                        if (_clients.Count >= MaxConcurrentClients) rejected = true;
                        else _clients.Add(client);
                    }

                    if (rejected)
                    {
                        try { client.Close(); } catch (Exception) { }
                        LogEntryReceived?.Invoke(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Direction = LogDirection.System,
                            Message = $"Connection refused (limit {MaxConcurrentClients} reached): {clientIp}"
                        });
                        continue;
                    }

                    LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = $"Client connected: {clientIp}" });

                    // 각 클라이언트에 대한 처리는 별도의 백그라운드 작업으로 분리하여 실행합니다.
                    _ = HandleClientAsync(client, _cancellationTokenSource.Token);
                }
            }
            // Stop() 메서드 호출 시 정상적으로 발생하는 예외 처리
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                // 정상 중단
            }
            // 중지 중에 Accept가 깨어나며 나는 예외는 종류를 가리지 않고 정상 종료입니다.
            // (닫힌 리스너에서 나는 예외는 Windows/런타임 버전에 따라 ObjectDisposedException이기도 하고
            //  OperationAborted가 아닌 SocketException이기도 합니다. 이를 오류로 처리하면
            //  상태가 Error로 남고 '포트를 열 수 없습니다' 배너까지 잘못 뜹니다.)
            catch (Exception) when (_isStopping)
            {
                // 정상 중단
            }
            catch (SocketException ex)
            {
                string errorMessage;
                if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    errorMessage = $"Error: Port {port} is already in use.";
                else if (ex.SocketErrorCode == SocketError.AccessDenied)
                    errorMessage = "Error: Access denied. Please run as administrator.";
                else
                    errorMessage = $"Socket Error: {ex.Message}";

                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = errorMessage });

                // 모달 대화상자 대신 이벤트로 알립니다. ViewModel이 창 안에 인라인 배너로 표시합니다.
                StartFailed?.Invoke(errorMessage, $"SocketException {ex.ErrorCode} · {ex.SocketErrorCode}", ex.SocketErrorCode);

                StatusChanged?.Invoke("Error");
                IsRunning = false;
            }
            catch (Exception ex)
            {
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = $"Error: {ex.Message}" });
                Stop();
            }
        }

        /// <summary>
        /// 서버 리스닝을 중지하고 모든 클라이언트 연결을 종료합니다.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            // 리스너를 닫기 '전에' 표시해야, Accept가 깨어나며 던지는 예외를 정상 종료로 판단할 수 있습니다.
            _isStopping = true;

            StopPeriodicSend();
            _cancellationTokenSource?.Cancel();
            _listener?.Stop();

            lock (_clients)
            {
                foreach (var client in _clients) client.Close();
                _clients.Clear();
            }

            IsRunning = false;
            StatusChanged?.Invoke("Stopped");
            LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "Server stopped." });
        }

        /// <summary>
        /// 연결된 모든 클라이언트에게 데이터를 브로드캐스트합니다.
        /// </summary>
        public async Task SendToAllClientsAsync(string message, Encoding encoding)
        {
            if (!IsRunning)
            {
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "Server is not running." });
                return;
            }
            string parsedMessage = AsciiTagParser.Parse(message);
            byte[] data = encoding.GetBytes(parsedMessage);
            List<TcpClient> clientsCopy;
            lock (_clients) { clientsCopy = _clients.ToList(); }

            LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.Sent, Message = $"Broadcast to {clientsCopy.Count} client(s)", Data = data, Length = data.Length });

            foreach (var client in clientsCopy)
            {
                try
                {
                    if (client.Connected)
                    {
                        NetworkStream stream = client.GetStream();
                        await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = $"Failed to send to a client: {ex.Message}" });
                }
            }
        }

        /// <summary>
        /// 주기적 브로드캐스트를 시작합니다.
        /// </summary>
        public void StartPeriodicSend(string message, int interval, Encoding encoding)
        {
            if (_periodicTimer != null) StopPeriodicSend();
            _periodicTimer = new System.Timers.Timer(interval);
            _periodicTimer.Elapsed += async (sender, e) => await SendToAllClientsAsync(message, encoding);
            _periodicTimer.AutoReset = true;
            _periodicTimer.Enabled = true;
        }

        /// <summary>
        /// 주기적 브로드캐스트를 중지합니다.
        /// </summary>
        public void StopPeriodicSend()
        {
            if (_periodicTimer != null)
            {
                _periodicTimer.Enabled = false;
                _periodicTimer.Dispose();
                _periodicTimer = null;
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "Periodic broadcasting stopped." });
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 개별 클라이언트로부터 데이터를 수신하고, 타임아웃을 고려하여 데이터를 모은 뒤 처리합니다.
        /// </summary>
        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            NetworkStream? stream = null;
            string clientIp = (client.Client?.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "Unknown IP";
            bool hasRepliedOnce = false;
            string currentResponsePattern = ResponsePattern;

            try
            {
                stream = client.GetStream();

                // 1. "SendOnce" 패턴: 접속하자마자 데이터 전송
                if (currentResponsePattern == "SendOnce")
                {
                    string messageToSend = string.IsNullOrEmpty(ReplyMessage) ? "OK" : ReplyMessage;
                    string parsedMessage = AsciiTagParser.Parse(messageToSend);
                    byte[] welcomeMsg = CurrentEncoding.GetBytes(parsedMessage);
                    await stream.WriteAsync(welcomeMsg, 0, welcomeMsg.Length, token).ConfigureAwait(false);
                    LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.Sent, Message = $"To {clientIp}", Data = welcomeMsg, Length = welcomeMsg.Length });
                    currentResponsePattern = "ListenOnly"; // 전송 후 Listen 모드로 변경
                }

                // 2. 데이터 수신 및 누적(Accumulation) 로직
                byte[] readBuffer = new byte[8192]; // 읽기용 임시 버퍼
                List<byte> accumulatedData = new List<byte>(); // 데이터를 모을 리스트

                while (!token.IsCancellationRequested)
                {
                    // A. 첫 번째 데이터 패킷 대기 (Blocking)
                    int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, token).ConfigureAwait(false);
                    if (bytesRead == 0) break; // 연결 종료

                    // 첫 데이터 저장
                    // ArraySegment는 ICollection<byte>이므로 List.AddRange가 통째로 복사합니다.
                    // readBuffer.Take(n)을 쓰면 바이트 하나씩 열거해 1 MB 메시지에서 수십 배 느려집니다.
                    accumulatedData.AddRange(new ArraySegment<byte>(readBuffer, 0, bytesRead));

                    // B. 추가 데이터 대기 (침묵 감지)
                    // 설정된 ReceiveTimeout 시간 동안 추가 데이터가 들어오는지 확인
                    if (ReceiveTimeout > 0)
                    {
                        DateTime lastDataTime = DateTime.Now;

                        // 마지막 데이터 수신 후 Timeout이 지날 때까지 루프.
                        // [보안] 프레임 상한에 닿으면 침묵을 더 기다리지 않고 즉시 빠져나와 처리합니다.
                        // 그래야 침묵 없이 계속 쏟아붓는 클라이언트에서 누적이 무한히 커지지 않습니다.
                        while ((DateTime.Now - lastDataTime).TotalMilliseconds < ReceiveTimeout
                               && accumulatedData.Count < MaxAccumulatedBytes)
                        {
                            if (stream.DataAvailable)
                            {
                                // 데이터가 있으면 즉시 읽어서 합침
                                bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, token).ConfigureAwait(false);
                                if (bytesRead == 0) break;

                                accumulatedData.AddRange(new ArraySegment<byte>(readBuffer, 0, bytesRead));

                                // 데이터를 받았으므로 타이머 리셋 (연속 데이터 수신 대기)
                                lastDataTime = DateTime.Now;
                            }
                            else
                            {
                                // 데이터가 없으면 아주 짧게 대기하여 CPU 과부하 방지
                                await Task.Delay(10, token).ConfigureAwait(false);
                            }
                        }
                    }

                    // C. 모아진 데이터 처리 (accumulatedData 사용)
                    byte[] finalData = accumulatedData.ToArray();
                    LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.Received, Message = clientIp, Data = finalData, Length = finalData.Length });

                    // --- 응답 로직 시작 ---

                    // [우선순위 1] 규칙 기반 응답 (Rules)
                    bool ruleMatched = false;
                    if (Rules != null && Rules.Count > 0)
                    {
                        string receivedDataStr = CurrentEncoding.GetString(finalData);
                        var matchedRule = Rules.FirstOrDefault(r => !string.IsNullOrEmpty(r.ReceiveData) && receivedDataStr.Contains(r.ReceiveData));

                        if (matchedRule != null)
                        {
                            string parsedReply = AsciiTagParser.Parse(matchedRule.SendData ?? string.Empty);
                            byte[] sendBytes = CurrentEncoding.GetBytes(parsedReply);
                            await stream.WriteAsync(sendBytes, 0, sendBytes.Length, token).ConfigureAwait(false);
                            LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.Sent, Message = $"Rule to {clientIp}", Data = sendBytes, Length = sendBytes.Length });
                            ruleMatched = true;
                        }
                    }

                    // [우선순위 2] 수신 후 응답 (ReplyAfterReceive)
                    if (!ruleMatched && currentResponsePattern == "ReplyAfterReceive")
                    {
                        // 1회성 응답이거나, 지속 응답(Endless)일 경우
                        if (!hasRepliedOnce || IsReplyEndless)
                        {
                            if (!string.IsNullOrEmpty(ReplyMessage))
                            {
                                string parsedReply = AsciiTagParser.Parse(ReplyMessage);
                                byte[] replyMsg = CurrentEncoding.GetBytes(parsedReply);
                                await stream.WriteAsync(replyMsg, 0, replyMsg.Length, token).ConfigureAwait(false);
                                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.Sent, Message = $"Reply to {clientIp}", Data = replyMsg, Length = replyMsg.Length });
                            }

                            hasRepliedOnce = true;

                            // 지속 응답이 아니면 모드 변경 (더 이상 응답 안 함)
                            if (!IsReplyEndless)
                            {
                                currentResponsePattern = "ListenOnly";
                            }
                        }
                    }
                    // [우선순위 3] 에코 (Echo)
                    else if (!ruleMatched && currentResponsePattern == "Echo")
                    {
                        await stream.WriteAsync(finalData, 0, finalData.Length, token).ConfigureAwait(false);
                        LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.Sent, Message = $"Echo to {clientIp}", Data = finalData, Length = finalData.Length });
                    }

                    // D. 다음 수신을 위해 버퍼 초기화
                    accumulatedData.Clear();
                }
            }
            catch (OperationCanceledException) { /* 정상 취소 */ }
            catch (Exception ex)
            {
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = $"Client Error: {ex.Message}" });
            }
            finally
            {
                client.Close();
                lock (_clients) { _clients.Remove(client); }
                LogEntryReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = $"Client disconnected: {clientIp}" });
            }
        }

        #endregion
    }
}