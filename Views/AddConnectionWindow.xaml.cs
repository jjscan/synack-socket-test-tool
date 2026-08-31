using Microsoft.Win32;
using SocketTestTool.Models;
using SocketTestTool.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SocketTestTool.Views
{
    public enum ConnectionCheckStatus { Success, PortClosed, HostUnreachable }

    /// <summary>
    /// 연결을 새로 만들거나 기존 연결을 수정하는 대화상자입니다. (목업 1d)
    /// </summary>
    public partial class AddConnectionWindow : Window
    {
        #region Fields

        private bool _isServerMode;
        private readonly ObservableCollection<ConnectionModel>? _allConnections;
        private readonly ConnectionModel? _editingConnection;

        #endregion

        #region Result Properties

        public string IpAddress { get; private set; } = string.Empty;
        public int Port { get; private set; }
        public string ResponsePattern { get; private set; } = string.Empty;
        public ObservableCollection<ResponseRule> Rules { get; set; }
        public string ReplyMessage { get; private set; } = string.Empty;
        public bool IsReplyEndless { get; private set; }
        public bool IsRealtimeLogEnabled { get; private set; }
        public string LogFilePath { get; private set; } = string.Empty;
        public string EncodingName { get; private set; } = "ASCII";
        public int ReceiveTimeout { get; private set; }
        public bool IsForwardingEnabled { get; private set; }
        public string ForwardIpAddress { get; private set; } = string.Empty;
        public int ForwardPort { get; private set; }

        /// <summary>
        /// 대화상자에서 최종적으로 선택된 연결 종류입니다.
        /// 새 연결을 만들 때는 사용자가 세그먼트로 바꿀 수 있습니다.
        /// </summary>
        public bool IsServerMode => _isServerMode;

        #endregion

        #region Constructor

        public AddConnectionWindow(bool isServer, ConnectionModel? existingConnection = null, ObservableCollection<ConnectionModel>? allConnections = null)
        {
            InitializeComponent();

            _isServerMode = isServer;
            _editingConnection = existingConnection;
            _allConnections = allConnections; // MainViewModel로부터 Connections 목록을 받음

            Rules = new ObservableCollection<ResponseRule>();
            Rules.CollectionChanged += (s, e) => UpdateRuleCount();
            RulesItemsControl.ItemsSource = Rules;

            // 세그먼트 초기 상태. 수정 중에는 종류를 바꿀 수 없습니다.
            ServerModeRadio.IsChecked = isServer;
            ClientModeRadio.IsChecked = !isServer;
            if (existingConnection != null)
            {
                Title = "연결 수정 Edit Connection";
                OkButton.Content = "저장 Save";
                ServerModeRadio.IsEnabled = false;
                ClientModeRadio.IsEnabled = false;
            }

            ApplyModeVisibility();

            if (isServer)
            {
                // 서버는 모든 네트워크 인터페이스에서 접속을 받을 수 있도록 0.0.0.0(IPAddress.Any)을 기본값으로 사용합니다.
                // (127.0.0.1로 바인딩하면 루프백 이외의 클라이언트가 접속할 수 없습니다.)
                IpTextBox.Text = IPAddress.Any.ToString();
            }

            if (existingConnection != null)
            {
                LoadFrom(existingConnection);
            }
            else
            {
                SuggestDefaultLogPath();
            }

            UpdateRuleCount();
        }

        #endregion

        #region Initialization Helpers

        /// <summary>
        /// 기존 연결의 설정값을 각 입력 컨트롤에 채워 넣습니다.
        /// </summary>
        private void LoadFrom(ConnectionModel existing)
        {
            IpTextBox.Text = existing.IpAddress;
            PortTextBox.Text = existing.Port.ToString();
            LogFilePathTextBox.Text = existing.LogFilePath;
            LogOnRadio.IsChecked = existing.IsRealtimeLogEnabled;

            SelectResponsePattern(existing.ResponsePattern ?? "Echo");

            if (existing.Manager is TcpServerManager manager)
            {
                ReplyMessageTextBox.Text = manager.ReplyMessage;
                EndlessReplyCheckBox.IsChecked = manager.IsReplyEndless;
                ReceiveTimeoutTextBox.Text = manager.ReceiveTimeout.ToString();
            }
            else
            {
                ReplyMessageTextBox.Text = existing.ReplyMessage;
                EndlessReplyCheckBox.IsChecked = existing.IsReplyEndless;
                if (existing.ReceiveTimeout > 0) ReceiveTimeoutTextBox.Text = existing.ReceiveTimeout.ToString();
            }

            if (existing.Rules != null)
            {
                foreach (var rule in existing.Rules) Rules.Add(rule);
            }

            // 수신 데이터 자동 전달 설정 불러오기
            ForwardingCheckBox.IsChecked = existing.IsForwardingEnabled;
            if (!string.IsNullOrWhiteSpace(existing.ForwardIpAddress)) ForwardIpTextBox.Text = existing.ForwardIpAddress;
            if (existing.ForwardPort > 0) ForwardPortTextBox.Text = existing.ForwardPort.ToString();
        }

        /// <summary>
        /// 새 연결일 때 기본 로그 파일 경로를 제안합니다.
        /// </summary>
        private void SuggestDefaultLogPath()
        {
            string type = _isServerMode ? "Server" : "Client";
            string ip = IpTextBox.Text.Replace(".", "_");
            string port = PortTextBox.Text;
            string defaultFileName = $"{type}_{ip}_{port}_{DateTime.Now:yyyyMMdd}.log";
            string defaultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            LogFilePathTextBox.Text = Path.Combine(defaultDir, defaultFileName);
        }

        /// <summary>
        /// 응답 패턴 코드값에 해당하는 라디오 카드를 선택합니다.
        /// </summary>
        private void SelectResponsePattern(string pattern)
        {
            // 클라이언트에는 Echo와 '접속 시 1회 전송' 카드가 없습니다.
            // 이 기능이 생기기 전에 만든 클라이언트 연결은 기본값 "Echo"로 저장돼 있으므로,
            // 그대로 두면 아무것도 선택되지 않은 것처럼 보입니다.
            if (!_isServerMode && (pattern == "Echo" || pattern == "SendOnce"))
            {
                pattern = "ListenOnly";
            }

            switch (pattern)
            {
                case "ReplyAfterReceive": ReplyAfterReceiveRadio.IsChecked = true; break;
                case "SendOnce": SendOnceRadio.IsChecked = true; break;
                case "ListenOnly": ListenOnlyRadio.IsChecked = true; break;
                default: EchoRadio.IsChecked = true; break;
            }
        }

        /// <summary>
        /// 현재 선택된 응답 패턴 코드값을 돌려줍니다.
        /// </summary>
        private string GetSelectedResponsePattern()
        {
            if (ReplyAfterReceiveRadio.IsChecked == true) return "ReplyAfterReceive";
            if (SendOnceRadio.IsChecked == true) return "SendOnce";
            if (ListenOnlyRadio.IsChecked == true) return "ListenOnly";
            return "Echo";
        }

        /// <summary>
        /// 서버/클라이언트 모드에 맞춰 응답 설정의 표시 내용을 바꿉니다.
        /// 응답 설정 자체는 양쪽 모두에 있고, 클라이언트에서는 쓸 수 없는 항목만 감춥니다.
        /// </summary>
        private void ApplyModeVisibility()
        {
            ResponseOptionsPanel.Visibility = Visibility.Visible;

            ResponseGroupLabel.Text = _isServerMode
                ? "응답 패턴 Response Pattern"
                : "자동 응답 Auto Reply";

            // Echo와 '접속 시 1회 전송'은 서버에서만 의미가 있습니다.
            // 특히 Echo는 상대 서버도 Echo면 둘이 무한히 주고받게 되므로 클라이언트에 두지 않습니다.
            var serverOnly = _isServerMode ? Visibility.Visible : Visibility.Collapsed;
            EchoRadio.Visibility = serverOnly;
            SendOnceRadio.Visibility = serverOnly;

            // 클라이언트에서 서버 전용 패턴이 선택된 채로 남지 않게 합니다.
            if (!_isServerMode && (EchoRadio.IsChecked == true || SendOnceRadio.IsChecked == true))
            {
                ListenOnlyRadio.IsChecked = true;
            }
            // 서버로 돌아왔는데 아무것도 선택돼 있지 않으면 기본값으로 되돌립니다.
            else if (_isServerMode && GetSelectedResponsePattern() == "Echo" && EchoRadio.IsChecked != true)
            {
                EchoRadio.IsChecked = true;
            }

            UpdateReceiveTimeoutVisibility();
        }

        /// <summary>
        /// '수신 대기'는 조각을 합쳐 한 건으로 판정할 때 쓰입니다.
        /// 고정 응답을 쓸 때와, 규칙이 하나라도 있을 때 의미가 있습니다.
        /// (규칙은 '수동 응답'에서도 동작하므로 응답 패턴만으로 판단하면 안 됩니다.)
        /// </summary>
        private void UpdateReceiveTimeoutVisibility()
        {
            if (ReceiveTimeoutPanel == null) return;

            bool needed = ReplyAfterReceiveRadio.IsChecked == true || Rules.Count > 0;
            ReceiveTimeoutPanel.Visibility = needed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateRuleCount()
        {
            RuleCountText.Text = Rules.Count == 1 ? "1 rule" : $"{Rules.Count} rules";
            UpdateReceiveTimeoutVisibility();
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// 서버/클라이언트 세그먼트를 바꿨을 때 호출됩니다.
        /// </summary>
        private void ConnectionMode_Changed(object sender, RoutedEventArgs e)
        {
            // InitializeComponent 도중에도 Checked가 발생하므로 컨트롤 생성 여부를 확인합니다.
            if (ResponseOptionsPanel == null) return;

            _isServerMode = ServerModeRadio.IsChecked == true;
            ApplyModeVisibility();

            // 새 연결일 때만 기본 주소를 모드에 맞게 바꿔 줍니다.
            if (_editingConnection == null)
            {
                IpTextBox.Text = _isServerMode ? IPAddress.Any.ToString() : "127.0.0.1";
                SuggestDefaultLogPath();
            }

            ResetCheckResult();
        }

        /// <summary>
        /// 응답 패턴 카드를 바꿨을 때, '수신 후 응답' 전용 옵션의 표시 여부를 갱신합니다.
        /// </summary>
        private void ResponsePattern_Changed(object sender, RoutedEventArgs e)
        {
            if (ReplyOptionsPanel == null) return;

            ReplyOptionsPanel.Visibility = ReplyAfterReceiveRadio.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;

            UpdateReceiveTimeoutVisibility();
        }

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ReceiveRuleTextBox.Text) && string.IsNullOrWhiteSpace(SendRuleTextBox.Text))
            {
                ShowCheckResult(false, "규칙의 'Receive' 또는 'Send' 값을 입력하세요.");
                return;
            }

            Rules.Add(new ResponseRule { ReceiveData = ReceiveRuleTextBox.Text, SendData = SendRuleTextBox.Text });
            ReceiveRuleTextBox.Clear();
            SendRuleTextBox.Clear();
        }

        private void RemoveRule_Click(object sender, RoutedEventArgs e)
        {
            // 각 행의 ✕ 버튼은 Tag에 자기 규칙을 담고 있습니다.
            if (sender is Button button && button.Tag is ResponseRule rule)
            {
                Rules.Remove(rule);
            }
        }

        private void BrowseLogFile_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Log File (*.log)|*.log|Text File (*.txt)|*.txt",
                Title = "Save Log File As...",
                FileName = Path.GetFileName(LogFilePathTextBox.Text)
            };

            if (sfd.ShowDialog() == true)
            {
                LogFilePathTextBox.Text = sfd.FileName;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PortTextBox.Text, out int port) || string.IsNullOrWhiteSpace(IpTextBox.Text))
            {
                ShowCheckResult(false, "올바른 IP 주소와 포트 번호를 입력하세요.");
                return;
            }

            IpAddress = IpTextBox.Text;
            Port = port;
            ResponsePattern = GetSelectedResponsePattern();

            if (ResponsePattern == "ReplyAfterReceive")
            {
                ReplyMessage = ReplyMessageTextBox.Text;
                IsReplyEndless = EndlessReplyCheckBox.IsChecked == true;
            }

            // 조각 합치기는 고정 응답뿐 아니라 규칙만 쓰는 경우에도 필요합니다.
            // 입력란이 보이지 않는 설정에서는 0(합치지 않음)으로 둡니다.
            if (ReceiveTimeoutPanel.Visibility == Visibility.Visible)
            {
                if (int.TryParse(ReceiveTimeoutTextBox.Text, out int timeout) && timeout >= 0) ReceiveTimeout = timeout;
                else ReceiveTimeout = 300;
            }
            else
            {
                ReceiveTimeout = 0;
            }

            IsRealtimeLogEnabled = LogOnRadio.IsChecked == true;
            if (IsRealtimeLogEnabled)
            {
                LogFilePath = LogFilePathTextBox.Text;

                // [보안] 시스템·시작프로그램 등 보호된 위치는 로그 대상으로 허용하지 않습니다.
                if (!LogService.IsPathAllowed(LogFilePath))
                {
                    ShowCheckResult(false, "로그 경로가 시스템·시작프로그램 등 보호된 위치입니다. 사용자 폴더로 바꾸세요.");
                    return;
                }
            }

            // 수신 데이터 자동 전달 설정 검증
            IsForwardingEnabled = ForwardingCheckBox.IsChecked == true;
            if (IsForwardingEnabled)
            {
                if (!IPAddress.TryParse(ForwardIpTextBox.Text, out IPAddress? forwardIp) || Equals(forwardIp, IPAddress.Any))
                {
                    ShowCheckResult(false, "자동 전달 대상 IP 주소가 올바르지 않습니다.");
                    return;
                }

                if (!int.TryParse(ForwardPortTextBox.Text, out int forwardPort) || forwardPort < 1 || forwardPort > 65535)
                {
                    ShowCheckResult(false, "자동 전달 대상 포트는 1~65535 사이여야 합니다.");
                    return;
                }

                // 자기 자신에게 전달하면 받은 데이터를 무한히 되돌려 보내게 되므로 막습니다.
                if (_isServerMode && forwardPort == port &&
                    (ForwardIpTextBox.Text == IpAddress || IPAddress.IsLoopback(forwardIp) && IpAddress == IPAddress.Any.ToString()))
                {
                    ShowCheckResult(false, "자동 전달 대상이 이 서버 자신입니다. 다른 주소나 포트를 지정하세요.");
                    return;
                }

                ForwardIpAddress = ForwardIpTextBox.Text;
                ForwardPort = forwardPort;
            }

            DialogResult = true;
        }

        /// <summary>
        /// 'Check' 버튼: 서버 모드면 포트를 열 수 있는지, 클라이언트 모드면 접속되는지 확인합니다.
        /// 결과는 모달 대신 대화상자 안에 인라인으로 보여 줍니다. (목업 1d)
        /// </summary>
        private async void CheckConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PortTextBox.Text, out int port) || string.IsNullOrWhiteSpace(IpTextBox.Text))
            {
                ShowCheckResult(false, "올바른 IP 주소와 포트를 입력하세요.");
                return;
            }

            string ip = IpTextBox.Text;
            TrafficLight.Fill = (Brush)FindResource("WarningBrush");
            ShowCheckResult(null, "확인 중... Checking connection...");
            this.IsEnabled = false;

            try
            {
                if (_isServerMode)
                {
                    await CheckAsServerAsync(ip, port);
                }
                else
                {
                    await CheckAsClientAsync(ip, port);
                }
            }
            finally
            {
                this.IsEnabled = true;
            }
        }

        #endregion

        #region Connection Check

        /// <summary>
        /// 서버 모드 확인: 다른 연결 설정과 충돌하는지, 포트를 실제로 열 수 있는지 봅니다.
        /// </summary>
        private async Task CheckAsServerAsync(string ip, int port)
        {
            // 1. 다른 '설정'과의 충돌 검사
            if (_allConnections != null)
            {
                var conflictingConn = _allConnections.FirstOrDefault(c =>
                    c != _editingConnection && // 자기 자신은 제외
                    c.Type == "Server" &&
                    c.IpAddress == ip &&
                    c.Port == port);

                if (conflictingConn != null)
                {
                    TrafficLight.Fill = (Brush)FindResource("DangerBrush");
                    ShowCheckResult(false, $"설정이 충돌합니다 — Seq {conflictingConn.Seq}와 같은 주소·포트입니다.");
                    return;
                }
            }

            // 2. 다른 '프로세스'와의 포트 점유 검사
            bool isAvailable = await CheckPortAvailability(ip, port);
            TrafficLight.Fill = (Brush)FindResource(isAvailable ? "SuccessBrush" : "DangerBrush");

            if (!isAvailable)
            {
                string? owner = await PortOwnerLookup.FindOwnerShortAsync(port);
                ShowCheckResult(false, owner == null
                    ? $"TCP {port} busy — 다른 프로세스가 사용 중입니다."
                    : $"TCP {port} busy — 사용 중: {owner}");
                return;
            }

            // 특정 주소에 바인딩할 때만 그 호스트의 응답 시간이 의미가 있습니다.
            string prefix = string.Empty;
            if (!string.Equals(ip, IPAddress.Any.ToString(), StringComparison.Ordinal))
            {
                long? rtt = await MeasureIcmpAsync(ip);
                if (rtt.HasValue) prefix = $"ICMP {rtt.Value} ms · ";
            }

            ShowCheckResult(true, $"{prefix}TCP {port} free — 포트 사용 가능합니다.");
        }

        /// <summary>
        /// 클라이언트 모드 확인: 대상 호스트·포트에 실제로 접속되는지 봅니다.
        /// </summary>
        private async Task CheckAsClientAsync(string ip, int port)
        {
            long? rtt = await MeasureIcmpAsync(ip);
            string prefix = rtt.HasValue ? $"ICMP {rtt.Value} ms · " : string.Empty;

            var tcpStatus = await CheckTcpConnection(ip, port);

            if (tcpStatus == ConnectionCheckStatus.Success)
            {
                TrafficLight.Fill = (Brush)FindResource("SuccessBrush");
                ShowCheckResult(true, $"{prefix}TCP {port} open — 접속에 성공했습니다.");
            }
            else if (tcpStatus == ConnectionCheckStatus.PortClosed)
            {
                TrafficLight.Fill = (Brush)FindResource("WarningBrush");
                ShowCheckResult(false, $"{prefix}TCP {port} closed — 호스트에는 닿지만 포트가 닫혀 있습니다.");
            }
            else if (rtt.HasValue)
            {
                TrafficLight.Fill = (Brush)FindResource("WarningBrush");
                ShowCheckResult(false, $"{prefix}TCP {port} unreachable — 방화벽에 막혀 있을 수 있습니다.");
            }
            else
            {
                TrafficLight.Fill = (Brush)FindResource("DangerBrush");
                ShowCheckResult(false, "호스트에 닿지 못했습니다 — Host is unreachable.");
            }
        }

        /// <summary>
        /// 확인 결과를 대화상자 안의 인라인 영역에 표시합니다.
        /// </summary>
        /// <param name="success">true=성공(초록), false=실패(빨강), null=진행 중(회색)</param>
        private void ShowCheckResult(bool? success, string message)
        {
            CheckResultPanel.Visibility = Visibility.Visible;
            StatusText.Text = message;

            if (success == true)
            {
                CheckResultIcon.Text = "✓";
                CheckResultIcon.Foreground = (Brush)FindResource("SuccessBrush");
            }
            else if (success == false)
            {
                CheckResultIcon.Text = "✕";
                CheckResultIcon.Foreground = (Brush)FindResource("DangerBrush");
            }
            else
            {
                CheckResultIcon.Text = "⋯";
                CheckResultIcon.Foreground = (Brush)FindResource("TextMutedBrush");
            }
        }

        /// <summary>
        /// 주소나 모드가 바뀌면 이전 확인 결과는 더 이상 유효하지 않으므로 지웁니다.
        /// </summary>
        private void ResetCheckResult()
        {
            CheckResultPanel.Visibility = Visibility.Collapsed;
            TrafficLight.Fill = (Brush)FindResource("StoppedDotBrush");
        }

        /// <summary>
        /// 해당 주소·포트로 리스너를 열 수 있는지 확인합니다.
        /// </summary>
        private Task<bool> CheckPortAvailability(string ip, int port)
        {
            // 소켓 바인딩은 상황에 따라 수 초간 멈출 수 있으므로 UI 스레드 밖에서 실행합니다.
            return Task.Run(() =>
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Parse(ip), port);
                    listener.Start();
                    listener.Stop();
                    return true;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    return false;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// ICMP 핑을 보내 왕복 시간(ms)을 잽니다. 응답이 없으면 null을 돌려줍니다.
        /// </summary>
        private async Task<long?> MeasureIcmpAsync(string ip)
        {
            using (var pinger = new Ping())
            {
                // 몇 번 보내 그중 가장 빠른 응답을 취합니다. (첫 핑은 ARP 등으로 느릴 수 있음)
                long? best = null;

                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        var reply = await pinger.SendPingAsync(ip, 1000);
                        if (reply.Status == IPStatus.Success)
                        {
                            if (!best.HasValue || reply.RoundtripTime < best.Value) best = reply.RoundtripTime;
                        }
                    }
                    catch (PingException) { /* 대상이 ICMP를 막아 둔 경우 등 */ }
                    catch (ArgumentException) { /* 호스트 문자열이 핑 대상이 될 수 없는 경우 */ }
                }

                return best;
            }
        }

        /// <summary>
        /// 대상 호스트·포트로 실제 TCP 접속을 시도해 결과를 분류합니다.
        /// </summary>
        private async Task<ConnectionCheckStatus> CheckTcpConnection(string ip, int port)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    var connectTask = client.ConnectAsync(ip, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask)
                    {
                        await connectTask;
                        return ConnectionCheckStatus.Success;
                    }

                    return ConnectionCheckStatus.HostUnreachable;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    return ConnectionCheckStatus.PortClosed;
                }
                catch
                {
                    return ConnectionCheckStatus.HostUnreachable;
                }
            }
        }

        #endregion
    }
}
