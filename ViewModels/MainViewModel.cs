using Microsoft.Win32;
using Newtonsoft.Json;
using SocketTestTool.Common;
using SocketTestTool.Models;
using SocketTestTool.Services;
using SocketTestTool.Views;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Concurrent;
using System.Windows.Threading;

namespace SocketTestTool.ViewModels
{
    /// <summary>
    /// MainWindow.xaml의 DataContext로 사용되는 메인 ViewModel 클래스입니다.
    /// 애플리케이션의 모든 상태와 로직을 관리합니다.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Fields
        // 네트워크 스레드에서 발생한 로그를 UI 스레드로 안전하게 전달하기 위한 중간 버퍼
        private readonly ConcurrentQueue<(ConnectionModel conn, LogEntry entry)> _logQueue = new ConcurrentQueue<(ConnectionModel, LogEntry)>();
        // UI 스레드에서 주기적으로 로그 버퍼를 처리하기 위한 타이머
        private readonly DispatcherTimer _uiUpdateTimer;
        // 연결별 최근 메시지 시각을 담아 두고 초당 메시지 수(msg/s)를 계산하는 창(window)입니다.
        private readonly Dictionary<string, Queue<DateTime>> _rateWindows = new Dictionary<string, Queue<DateTime>>();
        // msg/s 계산에 사용할 관측 구간(초)입니다.
        private const double RateWindowSeconds = 5.0;
        #endregion

        #region Properties

        /// <summary>
        /// 생성된 모든 소켓 연결의 목록입니다.
        /// </summary>
        public ObservableCollection<ConnectionModel> Connections { get; }

        private ConnectionModel _selectedConnection;
        /// <summary>
        /// 좌측 ListView에서 현재 선택된 단일 연결 객체입니다. (단일 선택 시에만 유효)
        /// </summary>
        public ConnectionModel SelectedConnection
        {
            get => _selectedConnection;
            set
            {
                if (_selectedConnection != value)
                {
                    if (_selectedConnection != null)
                        _selectedConnection.PropertyChanged -= SelectedConnection_PropertyChanged;

                    _selectedConnection = value;
                    OnPropertyChanged();

                    if (_selectedConnection != null)
                        _selectedConnection.PropertyChanged += SelectedConnection_PropertyChanged;

                    UpdateSendTextByteCount();
                    UpdatePeriodicSendTextByteCount();
                }
            }
        }

        /// <summary>
        /// 좌측 ListView에서 선택된 모든 항목의 컬렉션입니다. (다중 선택 지원)
        /// </summary>
        public ObservableCollection<object> SelectedItems { get; }

        private string _sendText;
        /// <summary>
        /// '1회 전송' 텍스트 박스와 바인딩된 속성입니다.
        /// </summary>
        public string SendText { get => _sendText; set { _sendText = value; OnPropertyChanged(); UpdateSendTextByteCount(); } }

        private string _periodicSendText;
        /// <summary>
        /// '주기적 전송' 텍스트 박스와 바인딩된 속성입니다.
        /// </summary>
        public string PeriodicSendText { get => _periodicSendText; set { _periodicSendText = value; OnPropertyChanged(); UpdatePeriodicSendTextByteCount(); } }

        private int _sendTextByteCount;
        public int SendTextByteCount { get => _sendTextByteCount; set { _sendTextByteCount = value; OnPropertyChanged(); } }

        private int _periodicSendTextByteCount;
        public int PeriodicSendTextByteCount { get => _periodicSendTextByteCount; set { _periodicSendTextByteCount = value; OnPropertyChanged(); } }

        private string _intervalText = "1000";
        public string IntervalText { get => _intervalText; set { _intervalText = value; OnPropertyChanged(); } }

        private string _searchText;
        public string SearchText { get => _searchText; set { _searchText = value; OnPropertyChanged(); } }

        /// <summary>
        /// 아스키 제어 문자 삽입 메뉴에 사용될 항목 목록입니다.
        /// </summary>
        public List<ControlCharacterItem> ControlCharacters { get; }

        /// <summary>
        /// 창 하단 상태 표시줄의 주 상태 메시지입니다.
        /// </summary>
        public string StatusText { get; private set; } = "Ready";

        /// <summary>
        /// 창 하단 상태 표시줄의 연결 통계 정보입니다.
        /// </summary>
        public string ConnectionStatsText { get; private set; }

        #endregion

        #region Fluent UI State

        /// <summary>
        /// 창 안에 인라인으로 표시되는 알림 배너 목록입니다. (목업 1f)
        /// </summary>
        public ObservableCollection<BannerItem> Banners { get; }

        /// <summary>
        /// 빈 상태 화면에 보여 줄 최근 세션 파일 목록입니다. (목업 1e)
        /// </summary>
        public ObservableCollection<string> RecentSessionPaths { get; }

        private bool _isTextView = true;
        /// <summary>
        /// 로그 본문을 해석된 문자열 그대로 보는 모드입니다.
        /// </summary>
        public bool IsTextView
        {
            get => _isTextView;
            set { if (_isTextView != value) { _isTextView = value; OnPropertyChanged(); } }
        }

        private bool _isSymbolView;
        /// <summary>
        /// 제어 문자를 [STX] 같은 태그로 바꿔 보는 모드입니다.
        /// </summary>
        public bool IsSymbolView
        {
            get => _isSymbolView;
            set { if (_isSymbolView != value) { _isSymbolView = value; OnPropertyChanged(); } }
        }

        private bool _isHexView;
        /// <summary>
        /// 본문 아래에 16진수 덤프를 함께 보는 모드입니다.
        /// </summary>
        public bool IsHexView
        {
            get => _isHexView;
            set { if (_isHexView != value) { _isHexView = value; OnPropertyChanged(); } }
        }

        private bool _isAutoScrollEnabled = true;
        /// <summary>
        /// 새 로그가 들어올 때 목록을 자동으로 맨 아래로 내릴지 여부입니다.
        /// </summary>
        public bool IsAutoScrollEnabled
        {
            get => _isAutoScrollEnabled;
            set { if (_isAutoScrollEnabled != value) { _isAutoScrollEnabled = value; OnPropertyChanged(); } }
        }

        private bool _isPeriodicMode;
        /// <summary>
        /// 컴포저가 '주기 전송' 탭에 있는지 여부입니다.
        /// </summary>
        public bool IsPeriodicMode
        {
            get => _isPeriodicMode;
            set
            {
                if (_isPeriodicMode != value)
                {
                    _isPeriodicMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsOnceMode));
                }
            }
        }

        /// <summary>
        /// 컴포저가 '1회 전송' 탭에 있는지 여부입니다. IsPeriodicMode의 반대입니다.
        /// </summary>
        public bool IsOnceMode
        {
            get => !_isPeriodicMode;
            set { if (value) IsPeriodicMode = false; }
        }

        private string _logCountText = "0";
        /// <summary>
        /// 검색 상자 오른쪽에 보이는 '일치/전체' 표시입니다. (예: "4/312")
        /// </summary>
        public string LogCountText
        {
            get => _logCountText;
            set { if (_logCountText != value) { _logCountText = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 상태 표시줄 오른쪽 끝에 보이는 버전 문자열입니다.
        /// 손으로 적어 두면 반드시 어긋나므로 어셈블리 버전에서 읽어 옵니다.
        /// (버전을 올리는 곳은 SocketTestTool.csproj 한 곳입니다. VERSIONING.md 참고)
        /// </summary>
        public string AppVersionText
        {
            get
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "v?" : $"v{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        /// <summary>메뉴에서 '시스템 설정 따르기'가 선택돼 있는지 여부입니다.</summary>
        public bool IsSystemTheme => ThemeService.Current == AppTheme.System;

        /// <summary>메뉴에서 '라이트'가 선택돼 있는지 여부입니다.</summary>
        public bool IsLightTheme => ThemeService.Current == AppTheme.Light;

        /// <summary>메뉴에서 '다크'가 선택돼 있는지 여부입니다.</summary>
        public bool IsDarkTheme => ThemeService.Current == AppTheme.Dark;

        #endregion

        #region Commands
        public ICommand AddServerCommand { get; }
        public ICommand AddClientCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand StartConnectionCommand { get; }
        public ICommand StopConnectionCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand PeriodicSendCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand SaveSessionCommand { get; }
        public ICommand LoadSessionCommand { get; }
        public ICommand SaveLogCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand ShowVersionHistoryCommand { get; }

        /// <summary>제어 문자 칩([STX] 등)을 현재 입력 상자에 덧붙입니다.</summary>
        public ICommand InsertControlCharacterCommand { get; }
        /// <summary>빈 상태 화면의 최근 세션 항목을 클릭했을 때 그 파일을 불러옵니다.</summary>
        public ICommand OpenRecentSessionCommand { get; }
        /// <summary>알림 배너를 닫습니다.</summary>
        public ICommand DismissBannerCommand { get; }
        /// <summary>테마를 바꿉니다. CommandParameter로 "System" / "Light" / "Dark"를 받습니다.</summary>
        public ICommand SetThemeCommand { get; }
        #endregion

        #region Constructor

        /// <summary>
        /// MainViewModel의 새 인스턴스를 초기화합니다.
        /// </summary>
        public MainViewModel()
        {
            // EUC-KR과 같은 추가 인코딩을 사용하기 위해 Provider를 등록합니다.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 속성 및 컬렉션 초기화
            Connections = new ObservableCollection<ConnectionModel>();
            SelectedItems = new ObservableCollection<object>();
            Banners = new ObservableCollection<BannerItem>();
            RecentSessionPaths = new ObservableCollection<string>();
            RefreshRecentSessions();

            // 이벤트 핸들러 구독
            SelectedItems.CollectionChanged += (s, e) => UpdateCommandStates();
            Connections.CollectionChanged += Connections_CollectionChanged;

            // 제어 문자 메뉴 항목 초기화
            ControlCharacters = new List<ControlCharacterItem>
            {
                new ControlCharacterItem { Name = "[STX] Start of Text", Tag = "[STX]" },
                new ControlCharacterItem { Name = "[ETX] End of Text", Tag = "[ETX]" },
                new ControlCharacterItem { Name = "[EOT] End of Transmission", Tag = "[EOT]" },
                new ControlCharacterItem { Name = "[ACK] Acknowledge", Tag = "[ACK]" },
                new ControlCharacterItem { Name = "[NAK] Negative Acknowledge", Tag = "[NAK]" },
                new ControlCharacterItem { Name = "[CR] Carriage Return", Tag = "[CR]" },
                new ControlCharacterItem { Name = "[LF] Line Feed", Tag = "[LF]" },
                new ControlCharacterItem { Name = "[NULL] Null", Tag = "[NULL]" }
            };

            // 커맨드 초기화
            AddServerCommand = new RelayCommand(ExecuteAddServer);
            AddClientCommand = new RelayCommand(ExecuteAddClient);
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteOnSingleSelected);
            RemoveCommand = new RelayCommand(ExecuteRemove, CanExecuteOnMultiSelected);
            StartConnectionCommand = new RelayCommand(ExecuteStartConnection, CanExecuteStart);
            StopConnectionCommand = new RelayCommand(ExecuteStopConnection, CanExecuteStop);
            SendCommand = new RelayCommand(ExecuteSend, CanExecuteWhenActive);
            PeriodicSendCommand = new RelayCommand(ExecutePeriodicSend, CanExecuteWhenActive);
            ClearLogCommand = new RelayCommand(ExecuteClearLog, CanExecuteOnSingleSelected);
            SaveSessionCommand = new RelayCommand(ExecuteSaveSession);
            LoadSessionCommand = new RelayCommand(ExecuteLoadSession);
            SaveLogCommand = new RelayCommand(ExecuteSaveLog, CanExecuteOnSingleSelected);
            ExitCommand = new RelayCommand(param => Application.Current.Shutdown());
            ShowVersionHistoryCommand = new RelayCommand(ExecuteShowVersionHistory);
            InsertControlCharacterCommand = new RelayCommand(ExecuteInsertControlCharacter);
            OpenRecentSessionCommand = new RelayCommand(ExecuteOpenRecentSession);
            DismissBannerCommand = new RelayCommand(param => DismissBanner(param as BannerItem));
            SetThemeCommand = new RelayCommand(ExecuteSetTheme);

            // 테마가 바뀌면 메뉴의 체크 표시를 다시 계산합니다.
            ThemeService.ThemeChanged += RaiseThemeSelectionChanged;

            // UI 업데이트 타이머 초기화 및 시작
            _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
            _uiUpdateTimer.Start();

            // 초기 상태바 정보 업데이트
            UpdateConnectionStats();
        }

        #endregion

        #region Command Methods

        /// <summary>
        /// 'Add Server' 커맨드가 실행될 때 호출됩니다.
        /// AddConnectionWindow를 서버 모드로 열고, 결과에 따라 새 서버 ConnectionModel을 생성하여 목록에 추가합니다.
        /// </summary>
        private void ExecuteAddServer(object param)
        {
            var dialog = new AddConnectionWindow(isServer: true, null, Connections);
            if (dialog.ShowDialog() == true) AddConnectionFromDialog(dialog);
        }

        /// <summary>
        /// 대화상자의 입력 결과로 새 연결을 만들어 목록에 추가합니다.
        /// 대화상자 안에서 서버/클라이언트를 바꿀 수 있으므로, 종류는 dialog.IsServerMode를 따릅니다.
        /// </summary>
        private void AddConnectionFromDialog(AddConnectionWindow dialog)
        {
            bool isServer = dialog.IsServerMode;

            // 서버일 때만, 동일한 IP/Port를 가진 서버가 이미 있는지 확인합니다.
            if (isServer)
            {
                bool isDuplicate = Connections.Any(c => c.Type == "Server" &&
                                                        c.IpAddress == dialog.IpAddress &&
                                                        c.Port == dialog.Port);
                if (isDuplicate)
                {
                    ShowBanner(new BannerItem
                    {
                        Severity = BannerSeverity.Warning,
                        Kind = "duplicate-server",
                        Title = $"같은 주소의 서버가 이미 있습니다 — {dialog.IpAddress}:{dialog.Port}",
                        Detail = "다른 IP나 포트를 사용하세요."
                    });
                    return;
                }
            }

            var connection = new ConnectionModel
            {
                Type = isServer ? "Server" : "Client",
                Address = $"{dialog.IpAddress}:{dialog.Port}",
                Status = "Stopped",
                IpAddress = dialog.IpAddress,
                Port = dialog.Port,
                EncodingName = dialog.EncodingName,
                IsForwardingEnabled = dialog.IsForwardingEnabled,
                ForwardIpAddress = dialog.ForwardIpAddress,
                ForwardPort = dialog.ForwardPort,
                IsRealtimeLogEnabled = dialog.IsRealtimeLogEnabled,
                LogFilePath = dialog.LogFilePath
            };

            if (isServer)
            {
                connection.ResponsePattern = dialog.ResponsePattern;
                connection.Rules = dialog.Rules.ToList();
                connection.ReplyMessage = dialog.ReplyMessage;
                connection.IsReplyEndless = dialog.IsReplyEndless;
                connection.ReceiveTimeout = dialog.ReceiveTimeout;
            }

            RefreshConnectionMeta(connection);
            Connections.Add(connection);
        }

        /// <summary>
        /// 'Add Client' 커맨드가 실행될 때 호출됩니다.
        /// AddConnectionWindow를 클라이언트 모드로 열고, 결과에 따라 새 클라이언트 ConnectionModel을 생성하여 목록에 추가합니다.
        /// </summary>
        private void ExecuteAddClient(object param)
        {
            var dialog = new AddConnectionWindow(isServer: false, null, Connections);
            if (dialog.ShowDialog() == true) AddConnectionFromDialog(dialog);
        }

        /// <summary>
        /// 'Edit' 커맨드가 실행될 때 호출됩니다.
        /// 선택된 연결의 이전 상태를 기억한 후, 설정을 수정하고 원래 실행 중이었다면 다시 시작합니다.
        /// </summary>
        private void ExecuteEdit(object param)
        {
            var selectedConnection = SelectedConnection;
            if (selectedConnection == null) return;

            bool wasRunning = selectedConnection.Status == "Listening" || selectedConnection.Status == "Connected";

            bool isServer = selectedConnection.Type == "Server";
            // 생성자에 전체 연결 목록(Connections)을 함께 전달합니다.
            var dialog = new AddConnectionWindow(isServer, selectedConnection, Connections);

            if (dialog.ShowDialog() == true) // 사용자가 OK를 눌렀을 경우
            {
                // 서버인 경우에만 중복 설정을 검사합니다.
                if (isServer)
                {
                    // 수정하려는 설정(dialog.IpAddress, dialog.Port)이
                    // 자기 자신을 제외한 다른 서버와 충돌하는지 확인합니다.
                    var conflictingConn = Connections.FirstOrDefault(c =>
                        c != selectedConnection && // 자기 자신은 검사 대상에서 제외
                        c.Type == "Server" &&
                        c.IpAddress == dialog.IpAddress &&
                        c.Port == dialog.Port);

                    if (conflictingConn != null)
                    {
                        ShowBanner(new BannerItem
                        {
                            Severity = BannerSeverity.Warning,
                            Kind = "duplicate-server",
                            ConnectionId = selectedConnection.Id,
                            Title = $"설정이 충돌합니다 — Seq {conflictingConn.Seq}와 같은 주소·포트입니다",
                            Detail = "다른 IP나 포트를 사용하세요. 변경 사항은 적용되지 않았습니다."
                        });

                        // 이 지점까지는 연결을 중지한 적이 없으므로, 실행 중이었다면 그대로 계속 실행됩니다.
                        // 여기서 StartConnection을 다시 호출하면 이전 Manager가 리스너를 쥔 채로 버려져
                        // 소켓이 회수되지 않고 포트도 계속 점유됩니다.
                        return; // 아무 설정도 반영하지 않고 Edit 작업을 중단합니다.
                    }
                }

                // 중복이 없을 때만 아래 로직을 실행합니다.
                if (wasRunning) StopConnection(selectedConnection);

                selectedConnection.IpAddress = dialog.IpAddress;
                selectedConnection.Port = dialog.Port;
                selectedConnection.Address = $"{dialog.IpAddress}:{dialog.Port}";
                selectedConnection.EncodingName = dialog.EncodingName;
                selectedConnection.IsForwardingEnabled = dialog.IsForwardingEnabled;
                selectedConnection.ForwardIpAddress = dialog.ForwardIpAddress;
                selectedConnection.ForwardPort = dialog.ForwardPort;
                selectedConnection.IsRealtimeLogEnabled = dialog.IsRealtimeLogEnabled;
                selectedConnection.LogFilePath = dialog.LogFilePath;
                if (isServer)
                {
                    selectedConnection.ResponsePattern = dialog.ResponsePattern;
                    selectedConnection.Rules = dialog.Rules.ToList();
                    selectedConnection.ReplyMessage = dialog.ReplyMessage;
                    selectedConnection.IsReplyEndless = dialog.IsReplyEndless;
                    selectedConnection.ReceiveTimeout = dialog.ReceiveTimeout;
                }

                RefreshConnectionMeta(selectedConnection);

                if (wasRunning)
                {
                    StartConnection(selectedConnection);
                    Log(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = $"Connection '{selectedConnection.Address}' updated and restarted." });
                }
                else
                {
                    UpdateCommandStates();
                }
            }
            // 사용자가 Cancel을 눌렀을 경우, 아무것도 하지 않아 원래 상태가 유지됩니다.
        }

        /// <summary>
        /// 'Remove' 커맨드가 실행될 때 호출됩니다.
        /// 선택된 모든 연결을 중지하고 목록에서 제거합니다.
        /// </summary>
        private void ExecuteRemove(object param)
        {
            if (SelectedItems == null || SelectedItems.Count == 0) return;
            foreach (var selected in SelectedItems.OfType<ConnectionModel>().ToList())
                StopAndRemoveConnection(selected);
        }

        /// <summary>
        /// 'Send Once' 커맨드가 실행될 때 호출됩니다.
        /// 선택된 연결로 데이터를 1회 전송합니다.
        /// </summary>
        private async void ExecuteSend(object param)
        {
            if (SelectedConnection == null || string.IsNullOrEmpty(SendText)) return;
            var encoding = GetEncodingByName(SelectedConnection.EncodingName);
            if (SelectedConnection.Manager is TcpClientManager client && client.IsConnected) await client.Send(SendText, encoding);
            else if (SelectedConnection.Manager is TcpServerManager server && server.IsRunning) await server.SendToAllClientsAsync(SendText, encoding);
            else Log(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "Not connected or server not running." });
        }

        /// <summary>
        /// 'Start/Stop Periodic' 커맨드가 실행될 때 호출됩니다.
        /// 주기적 전송 상태를 토글(시작/중지)합니다.
        /// </summary>
        private void ExecutePeriodicSend(object param)
        {
            if (SelectedConnection == null) return;
            if (!SelectedConnection.IsPeriodicSending)
            {
                if (string.IsNullOrEmpty(PeriodicSendText)) { MessageBox.Show("Please enter a message for periodic sending."); return; }
                if (!int.TryParse(IntervalText, out int interval) || interval <= 0) { MessageBox.Show("Please enter a valid interval in milliseconds."); return; }
                var encoding = GetEncodingByName(SelectedConnection.EncodingName);
                if (SelectedConnection.Manager is TcpClientManager client) client.StartPeriodicSend(PeriodicSendText, interval, encoding);
                else if (SelectedConnection.Manager is TcpServerManager server) server.StartPeriodicSend(PeriodicSendText, interval, encoding);
                SelectedConnection.IsPeriodicSending = true;
            }
            else
            {
                if (SelectedConnection.Manager is TcpClientManager client) client.StopPeriodicSend();
                if (SelectedConnection.Manager is TcpServerManager server) server.StopPeriodicSend();
                SelectedConnection.IsPeriodicSending = false;
            }
        }

        /// <summary>
        /// 'Clear Log' 커맨드가 실행될 때 호출됩니다.
        /// 선택된 연결의 로그와 통계 정보를 초기화합니다.
        /// </summary>
        private void ExecuteClearLog(object param)
        {
            if (SelectedConnection == null) return;
            SelectedConnection.Logs.Clear();
            SelectedConnection.BytesSent = 0;
            SelectedConnection.BytesReceived = 0;
        }

        /// <summary>
        /// 'Save Session' 커맨드가 실행될 때 호출됩니다.
        /// 현재 연결 목록을 JSON 파일로 저장합니다.
        /// </summary>
        private void ExecuteSaveSession(object param)
        {
            var sfd = new SaveFileDialog { Filter = "JSON File (*.json)|*.json", Title = "Save Session As..." };
            if (sfd.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(sfd.FileName, JsonConvert.SerializeObject(Connections, Formatting.Indented));
                RecentSessionService.Add(sfd.FileName);
                RefreshRecentSessions();

                int ruleSets = Connections.Count(c => c.Rules != null && c.Rules.Count > 0);
                var banner = new BannerItem
                {
                    Severity = BannerSeverity.Success,
                    Kind = "session-saved",
                    Title = $"세션을 저장했습니다 — {Path.GetFileName(sfd.FileName)}",
                    Detail = $"{Connections.Count} connections · {ruleSets} rule sets",
                    PrimaryActionText = "폴더 열기"
                };
                banner.PrimaryActionCommand = new RelayCommand(_ => RevealInExplorer(sfd.FileName));
                ShowBanner(banner);
            }
            catch (Exception ex)
            {
                ShowBanner(new BannerItem
                {
                    Severity = BannerSeverity.Error,
                    Kind = "session-save-failed",
                    Title = "세션을 저장하지 못했습니다",
                    Detail = ex.Message
                });
            }
        }

        /// <summary>
        /// 'Load Session' 커맨드가 실행될 때 호출됩니다.
        /// JSON 파일에서 연결 목록을 불러옵니다.
        /// </summary>
        private void ExecuteLoadSession(object param)
        {
            var ofd = new OpenFileDialog { Filter = "JSON File (*.json)|*.json", Title = "Load Session" };
            if (ofd.ShowDialog() == true) LoadSessionFile(ofd.FileName);
        }

        /// <summary>
        /// 지정한 경로의 세션 파일을 읽어 연결 목록을 교체합니다.
        /// 파일 대화상자와 '최근 세션' 목록 양쪽에서 함께 사용합니다.
        /// </summary>
        private void LoadSessionFile(string path)
        {
            try
            {
                var loadedConnections = JsonConvert.DeserializeObject<ObservableCollection<ConnectionModel>>(File.ReadAllText(path));
                if (loadedConnections == null) return;

                foreach (var conn in Connections) StopConnection(conn);
                Connections.Clear();
                Banners.Clear();

                foreach (var conn in loadedConnections)
                {
                    Connections.Add(conn);
                    RefreshConnectionMeta(conn);
                    if (conn.AutoStart) StartConnection(conn);
                }

                RecentSessionService.Add(path);
                RefreshRecentSessions();
            }
            catch (Exception ex)
            {
                ShowBanner(new BannerItem
                {
                    Severity = BannerSeverity.Error,
                    Kind = "session-load-failed",
                    Title = $"세션을 불러오지 못했습니다 — {Path.GetFileName(path)}",
                    Detail = ex.Message
                });
            }
        }

        /// <summary>
        /// 탐색기에서 해당 파일이 선택된 상태로 폴더를 엽니다.
        /// </summary>
        private static void RevealInExplorer(string path)
        {
            try
            {
                // 방금 우리가 저장한 파일만 대상으로 삼습니다. 존재하지 않으면 아무것도 하지 않습니다.
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

                // 인자를 문자열로 이어 붙이지 않고 ArgumentList로 전달해 인자 주입 여지를 없앱니다.
                // (전체 경로로 정규화해 상대 경로·..\ 도 흡수합니다.)
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                psi.ArgumentList.Add("/select,");
                psi.ArgumentList.Add(Path.GetFullPath(path));
                Process.Start(psi);
            }
            catch (Exception)
            {
                // 탐색기를 열지 못해도 앱 동작에는 영향이 없으므로 무시합니다.
            }
        }

        /// <summary>
        /// 'Save Log' 커맨드가 실행될 때 호출됩니다.
        /// 선택된 연결의 현재 로그를 텍스트 파일로 저장합니다.
        /// </summary>
        private void ExecuteSaveLog(object param)
        {
            if (SelectedConnection == null) return;
            var sfd = new SaveFileDialog { FileName = $"SocketLog_{SelectedConnection.Address.Replace(":", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.txt", Filter = "Text File (*.txt)|*.txt", Title = "Save Log As..." };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var logText = string.Join("\n", SelectedConnection.Logs.Select(l => l.DisplayMessage));
                    File.WriteAllText(sfd.FileName, logText);
                    MessageBox.Show("Log saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex) { MessageBox.Show($"Failed to save log: {ex.Message}"); }
            }
        }

        /// <summary>
        /// 'Start' 커맨드가 실행될 때 호출됩니다.
        /// 선택된 모든 연결 중 'Stopped' 상태인 것들을 시작합니다.
        /// </summary>
        private void ExecuteStartConnection(object param)
        {
            if (SelectedItems == null || SelectedItems.Count == 0) return;
            foreach (var selected in SelectedItems.OfType<ConnectionModel>())
            {
                if (selected.Status == "Stopped" || selected.Status == "Ready" || selected.Status == "Error")
                    StartConnection(selected);
            }
        }

        /// <summary>
        /// 'Stop' 커맨드가 실행될 때 호출됩니다.
        /// 선택된 모든 연결 중 활성화 상태인 것들을 중지합니다.
        /// </summary>
        private void ExecuteStopConnection(object param)
        {
            if (SelectedItems == null || SelectedItems.Count == 0) return;
            foreach (var selected in SelectedItems.OfType<ConnectionModel>())
            {
                if (selected.Status == "Connected" || selected.Status == "Listening" || selected.Status == "Starting")
                    StopConnection(selected);
            }
        }

        #endregion

        #region CanExecute Methods

        /// <summary>
        /// Command의 실행 가능 여부를 결정하는 Predicate<object> 메서드들을 포함합니다.
        /// 이 메서드들의 반환값(true/false)에 따라 UI의 버튼 등이 활성화/비활성화됩니다.
        /// </summary>

        /// <summary>
        /// 정확히 하나의 항목이 선택되었을 때만 true를 반환합니다. (Edit, Send 등 단일 대상 커맨드용)
        /// </summary>
        private bool CanExecuteOnSingleSelected(object param) => SelectedItems?.Count == 1;

        /// <summary>
        /// 하나 이상의 항목이 선택되었을 때 true를 반환합니다. (Remove 등 다중 대상 커맨드용)
        /// </summary>
        private bool CanExecuteOnMultiSelected(object param) => SelectedItems?.Count > 0;

        /// <summary>
        /// 선택된 항목들 중 하나라도 시작 가능한 상태("Stopped", "Ready", "Error")일 때 true를 반환합니다.
        /// </summary>
        private bool CanExecuteStart(object param)
        {
            if (SelectedItems == null || SelectedItems.Count == 0) return false;
            // OfType<ConnectionModel>() : SelectedItems가 object 컬렉션이므로, ConnectionModel 타입만 필터링합니다.
            // .Any(...) : 컬렉션의 항목 중 하나라도 조건을 만족하면 true를 반환합니다.
            return SelectedItems.OfType<ConnectionModel>().Any(conn => conn.Status == "Stopped" || conn.Status == "Ready" || conn.Status == "Error");
        }

        /// <summary>
        /// 선택된 항목들 중 하나라도 중지 가능한 상태("Connected", "Listening")일 때 true를 반환합니다.
        /// </summary>
        private bool CanExecuteStop(object param)
        {
            if (SelectedItems == null || SelectedItems.Count == 0) return false;
            return SelectedItems.OfType<ConnectionModel>().Any(conn => conn.Status == "Connected" || conn.Status == "Listening" || conn.Status == "Starting");
        }

        /// <summary>
        /// 단일 선택된 항목이 활성화 상태("Connected", "Listening")일 때 true를 반환합니다.
        /// </summary>
        private bool CanExecuteWhenActive(object param)
        {
            if (SelectedConnection == null) return false; // SelectedConnection은 단일 선택 시에만 유효
            return SelectedConnection.Status == "Connected" || SelectedConnection.Status == "Listening";
        }

        /// <summary>
        /// 모든 커맨드의 CanExecute 상태를 강제로 다시 평가하도록 UI에 신호를 보냅니다.
        /// ListView의 선택이 변경되거나, 소켓의 상태가 변경될 때 호출되어 버튼의 활성화/비활성화 상태를 실시간으로 갱신합니다.
        /// </summary>
        public void UpdateCommandStates()
        {
            // UI 스레드가 아닌 다른 스레드에서 호출될 경우를 대비하여 Dispatcher를 사용합니다.
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                (EditCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RemoveCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (PeriodicSendCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SaveLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StartConnectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StopConnectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            });
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// UI 업데이트 타이머의 Tick 이벤트 핸들러입니다.
        /// 로그 큐에 쌓인 로그들을 UI에 일괄적으로 추가합니다.
        /// </summary>
        private void UiUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_logQueue.IsEmpty) return;
            while (_logQueue.TryDequeue(out var tuple))
            {
                tuple.conn.Logs.Add(tuple.entry);
                while (tuple.conn.Logs.Count > 1000)
                {
                    tuple.conn.Logs.RemoveAt(0);
                }

                // 송수신 통계도 UI 스레드인 여기에서 갱신합니다. (HandleLogEntry는 소켓 스레드에서 실행됨)
                if (tuple.entry.Direction == LogDirection.Received) tuple.conn.BytesReceived += tuple.entry.Length;
                else if (tuple.entry.Direction == LogDirection.Sent) tuple.conn.BytesSent += tuple.entry.Length;

                // 로그 순환 버퍼(1000건)와 무관하게 계속 누적되는 총 메시지 수입니다.
                tuple.conn.MessageCount++;

                // 처리량(msg/s) 계산용으로 데이터 메시지의 시각만 기록합니다.
                if (tuple.entry.Direction != LogDirection.System)
                {
                    if (!_rateWindows.TryGetValue(tuple.conn.Id, out var window))
                    {
                        window = new Queue<DateTime>();
                        _rateWindows[tuple.conn.Id] = window;
                    }
                    window.Enqueue(tuple.entry.Timestamp);
                }
            }

            UpdateThroughputRates();
        }

        /// <summary>
        /// Connections 컬렉션이 변경될 때 호출됩니다.
        /// Seq 번호 재정렬, 통계 업데이트, PropertyChanged 이벤트 구독/해지를 처리합니다.
        /// </summary>
        private void Connections_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateConnectionStats();
            ReorderSequence();
            if (e.NewItems != null) foreach (ConnectionModel item in e.NewItems) item.PropertyChanged += Connection_PropertyChanged;
            if (e.OldItems != null) foreach (ConnectionModel item in e.OldItems) item.PropertyChanged -= Connection_PropertyChanged;
        }

        /// <summary>
        /// Connections 컬렉션에 포함된 개별 ConnectionModel의 속성이 변경될 때 호출됩니다.
        /// </summary>
        private void Connection_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 연결 상태가 변경되면 통계를 다시 계산합니다.
            if (e.PropertyName == nameof(ConnectionModel.Status))
            {
                UpdateConnectionStats();
            }

            // 카드 두 번째 줄(요약)에 영향을 주는 속성들입니다.
            if (e.PropertyName == nameof(ConnectionModel.Status) ||
                e.PropertyName == nameof(ConnectionModel.IsPeriodicSending) ||
                e.PropertyName == nameof(ConnectionModel.EncodingName))
            {
                RefreshConnectionMeta(sender as ConnectionModel);
            }
        }

        /// <summary>
        /// 현재 선택된 ConnectionModel의 속성이 변경될 때 호출됩니다.
        /// </summary>
        private void SelectedConnection_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 인코딩 이름이 변경되면 텍스트 박스의 바이트 카운트를 다시 계산합니다.
            if (e.PropertyName == nameof(ConnectionModel.EncodingName))
            {
                UpdateSendTextByteCount();
                UpdatePeriodicSendTextByteCount();
            }
        }

        /// <summary>
        /// 'Help' 메뉴의 'Version History' 커맨드가 실행될 때 호출됩니다.
        /// </summary>
        private void ExecuteShowVersionHistory(object param)
        {
            var versionWindow = new VersionHistoryWindow();
            versionWindow.Owner = Application.Current.MainWindow; // 부모 창 설정
            versionWindow.ShowDialog();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Manager 클래스에서 LogEntry가 발생했을 때 호출되는 중앙 처리 메서드입니다.
        /// </summary>
        private void HandleLogEntry(ConnectionModel conn, LogEntry entry)
        {
            // 데이터 변환, 파일 쓰기, 로그 큐 추가, 통계 업데이트 등 모든 로그 관련 작업을 처리합니다.
            if (entry.Data != null && entry.Data.Length > 0)
            {
                // 원본 바이트는 그대로 두고, 표시용 문자열만 상한까지 만듭니다.
                // 전체를 디코딩하면 큰 메시지 한 건에 수 MB짜리 문자열이 여러 개 생겨
                // 메모리가 튀고 UI 렌더링이 멈춥니다. (LogEntry.DisplayByteLimit 참고)
                var encoding = GetEncodingByName(conn.EncodingName);
                string decoded = encoding.GetString(entry.Data, 0, Math.Min(entry.Data.Length, LogEntry.DisplayByteLimit));
                entry.DecodedData = decoded;
                entry.RenderedData = SymbolRenderer.Render(decoded);
            }
            if (conn.IsRealtimeLogEnabled) LogService.Write(conn.Id, entry);

            // 수신 데이터 자동 전달이 켜져 있으면, 받은 원본 바이트를 그대로 대상 서버로 흘려보냅니다.
            if (entry.Direction == LogDirection.Received && entry.Data != null && entry.Data.Length > 0
                && conn.Forwarder is ForwardingClient forwarder)
            {
                forwarder.Enqueue(entry.Data);
            }

            // 이 메서드는 소켓 백그라운드 스레드에서 호출됩니다.
            // 통계(BytesSent/BytesReceived)는 UI 바인딩 대상이므로 여기서 직접 건드리지 않고,
            // 로그 큐를 비우는 UI 타이머에서 UI 스레드로 함께 반영합니다.
            _logQueue.Enqueue((conn, entry));
        }

        /// <summary>
        /// Connections 컬렉션의 Seq 번호를 1부터 순서대로 다시 매깁니다.
        /// </summary>
        private void ReorderSequence()
        {
            for (int i = 0; i < Connections.Count; i++) Connections[i].Seq = i + 1;
        }

        /// <summary>
        /// 지정된 ConnectionModel에 대한 소켓 연결을 시작합니다.
        /// </summary>
        private void StartConnection(ConnectionModel conn)
        {
            // 아직 살아있는 Manager가 남아 있으면 먼저 정리합니다.
            // 정리 없이 새 Manager로 덮어쓰면 이전 리스너/연결이 참조를 잃은 채 계속 살아남아
            // 소켓과 포트가 회수되지 않습니다.
            if ((conn.Manager is TcpServerManager runningServer && runningServer.IsRunning) ||
                (conn.Manager is TcpClientManager runningClient && runningClient.IsConnected))
            {
                StopConnection(conn);
            }

            // 다시 시작하는 것이므로 이전 실패 표시는 지웁니다.
            ClearBannersFor(conn);
            conn.ErrorText = null;
            RefreshConnectionMeta(conn);

            // 소켓 열기는 Task.Run으로 넘어가므로, 이 시점에는 Manager의 IsRunning이 아직 false입니다.
            // 상태를 지금 바로 'Starting'으로 바꿔 두지 않으면, 시작이 끝나기 전에 들어온 두 번째 시작 요청이
            // 위 가드를 그냥 통과해 같은 포트에 두 번 바인딩을 시도하고
            // 멀쩡한 서버에 '포트가 이미 사용 중' 오류와 가짜 배너가 뜹니다.
            conn.Status = "Starting";

            if (conn.IsRealtimeLogEnabled && !LogService.Initialize(conn))
            {
                // [보안] 로그 경로가 보호 위치(시스템·시작프로그램 등)라 거부되었거나 파일을 열지 못한 경우입니다.
                // 실시간 로깅을 꺼서, 이후 쓰기 시도와 오해를 부르는 상태를 정리하고 사용자에게 알립니다.
                conn.IsRealtimeLogEnabled = false;
                ShowBanner(new BannerItem
                {
                    Severity = BannerSeverity.Warning,
                    Kind = "log-path-rejected",
                    ConnectionId = conn.Id,
                    Title = $"로그 파일을 만들 수 없어 실시간 로깅을 껐습니다 — {conn.Address}",
                    Detail = "시스템·시작프로그램 등 보호된 위치이거나 접근할 수 없는 경로입니다. 로그 경로를 사용자 폴더로 바꾸세요.",
                    TechnicalDetail = conn.LogFilePath
                });
            }

            StartForwarder(conn);

            // 상태 변경은 소켓 백그라운드 스레드에서 올라오므로 UI 스레드로 넘겨야 합니다.
            // 이때 Invoke(동기) 대신 BeginInvoke(비동기)를 사용해, 소켓 스레드가 UI 스레드를 기다리지 않도록 합니다.
            Action<string> statusHandler = (status) => Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                conn.Status = status;

                // 연결이 끊어지거나 중지된 상태("Stopped")가 되면,
                // 주기적 전송 상태를 항상 false로 초기화합니다.
                if (status == "Stopped")
                {
                    conn.IsPeriodicSending = false;
                }

                UpdateCommandStates();
            }));

            if (conn.Type == "Server")
            {
                var serverManager = new TcpServerManager
                {
                    ResponsePattern = conn.ResponsePattern,
                    Rules = conn.Rules,
                    ReplyMessage = conn.ReplyMessage,
                    IsReplyEndless = conn.IsReplyEndless,
                    ReceiveTimeout = conn.ReceiveTimeout,
                    CurrentEncoding = GetEncodingByName(conn.EncodingName),
                    IsRealtimeLogEnabled = conn.IsRealtimeLogEnabled
                };
                conn.Manager = serverManager;
                serverManager.LogEntryReceived += (entry) => HandleLogEntry(conn, entry);
                serverManager.StatusChanged += statusHandler;

                // 실패 알림은 모달 대신 창 안의 배너로 보여 줍니다. (목업 1f)
                serverManager.StartFailed += (message, technical, error) =>
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        OnServerStartFailed(conn, message, technical, error)));

                // Task.Run으로 감싸서 소켓 바인딩과 Accept 루프가 UI 스레드에서 시작되지 않도록 합니다.
                // (UI 스레드에서 시작하면 이후의 모든 await 재개 지점이 UI 스레드로 돌아와 화면이 멈춥니다.)
                _ = Task.Run(() => serverManager.Start(conn.IpAddress, conn.Port));
            }
            else if (conn.Type == "Client")
            {
                var clientManager = new TcpClientManager
                {
                    CurrentEncoding = GetEncodingByName(conn.EncodingName),
                    IsRealtimeLogEnabled = conn.IsRealtimeLogEnabled
                };
                conn.Manager = clientManager;
                clientManager.LogEntryReceived += (entry) => HandleLogEntry(conn, entry);
                clientManager.StatusChanged += statusHandler;

                clientManager.ConnectionFailed += (message, isConnectFailure) =>
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        OnClientConnectionFailed(conn, message, isConnectFailure)));

                // 서버와 같은 이유로, 접속 시도와 수신 루프를 UI 스레드 밖에서 시작합니다.
                _ = Task.Run(() => clientManager.Connect(conn.IpAddress, conn.Port));
            }
        }

        /// <summary>
        /// 수신 데이터 자동 전달이 켜져 있으면 전달용 클라이언트를 만들어 시작합니다.
        /// </summary>
        private void StartForwarder(ConnectionModel conn)
        {
            // 이전에 만들어 둔 전달용 클라이언트가 남아 있으면 먼저 정리합니다.
            // (정리하지 않고 덮어쓰면 이전 클라이언트의 재접속 루프와 TCP 연결이 계속 살아남습니다.)
            StopForwarder(conn);

            if (!conn.IsForwardingEnabled || string.IsNullOrWhiteSpace(conn.ForwardIpAddress)) return;

            var forwarder = new ForwardingClient(conn.ForwardIpAddress, conn.ForwardPort);
            conn.Forwarder = forwarder;
            forwarder.LogEntryReceived += (entry) => HandleLogEntry(conn, entry);
            forwarder.Start();
        }

        /// <summary>
        /// 전달용 클라이언트가 있으면 중지하고 정리합니다.
        /// </summary>
        private void StopForwarder(ConnectionModel conn)
        {
            if (conn.Forwarder is ForwardingClient forwarder)
            {
                forwarder.Stop();
                conn.Forwarder = null;
            }
        }

        /// <summary>
        /// 지정된 ConnectionModel에 대한 소켓 연결을 중지합니다.
        /// </summary>
        private void StopConnection(ConnectionModel conn)
        {
            if (conn.IsRealtimeLogEnabled) LogService.Close(conn.Id);
            if (conn.Manager is TcpServerManager server) server.Stop();
            if (conn.Manager is TcpClientManager client) client.Disconnect();
            StopForwarder(conn);
            conn.IsPeriodicSending = false;

            // 아직 소켓이 열리기 전('Starting')이라면 Manager가 상태를 되돌려 주지 않으므로 직접 정리합니다.
            if (conn.Status == "Starting") conn.Status = "Stopped";
        }

        /// <summary>
        /// 연결을 중지하고 Connections 컬렉션에서 제거합니다.
        /// </summary>
        private void StopAndRemoveConnection(ConnectionModel conn)
        {
            StopConnection(conn);
            Connections.Remove(conn);
        }

        /// <summary>
        /// 시스템 메시지를 로그 큐에 추가합니다.
        /// </summary>
        private void Log(LogEntry entry)
        {
            var targetConn = SelectedConnection ?? Connections.FirstOrDefault();
            if (targetConn != null) Application.Current.Dispatcher.Invoke(() => _logQueue.Enqueue((targetConn, entry)));
        }

        /// <summary>
        /// 인코딩 이름(문자열)에 해당하는 Encoding 객체를 반환합니다.
        /// </summary>
        private Encoding GetEncodingByName(string encodingName)
        {
            switch (encodingName)
            {
                case "ASCII": return Encoding.ASCII;
                case "EUC-KR": return Encoding.GetEncoding("EUC-KR");
                case "UTF-8": default: return Encoding.UTF8;
            }
        }

        /// <summary>
        /// '1회 전송' 텍스트 박스의 현재 텍스트에 대한 바이트 카운트를 계산하고 업데이트합니다.
        /// </summary>
        private void UpdateSendTextByteCount()
        {
            if (string.IsNullOrEmpty(SendText) || SelectedConnection == null) { SendTextByteCount = 0; return; }
            var encoding = GetEncodingByName(SelectedConnection.EncodingName);
            var parsedText = AsciiTagParser.Parse(SendText);
            SendTextByteCount = encoding.GetByteCount(parsedText);
        }

        /// <summary>
        /// '주기적 전송' 텍스트 박스의 현재 텍스트에 대한 바이트 카운트를 계산하고 업데이트합니다.
        /// </summary>
        private void UpdatePeriodicSendTextByteCount()
        {
            if (string.IsNullOrEmpty(PeriodicSendText) || SelectedConnection == null) { PeriodicSendTextByteCount = 0; return; }
            var encoding = GetEncodingByName(SelectedConnection.EncodingName);
            var parsedText = AsciiTagParser.Parse(PeriodicSendText);
            PeriodicSendTextByteCount = encoding.GetByteCount(parsedText);
        }

        /// <summary>
        /// 모든 연결의 초당 메시지 수(msg/s)를 다시 계산합니다.
        /// 관측 구간(RateWindowSeconds)을 벗어난 오래된 기록은 버립니다.
        /// </summary>
        private void UpdateThroughputRates()
        {
            DateTime cutoff = DateTime.Now.AddSeconds(-RateWindowSeconds);

            foreach (var conn in Connections)
            {
                if (!_rateWindows.TryGetValue(conn.Id, out var window))
                {
                    conn.MessagesPerSecond = 0;
                    continue;
                }

                while (window.Count > 0 && window.Peek() < cutoff) window.Dequeue();
                conn.MessagesPerSecond = window.Count / RateWindowSeconds;
            }
        }

        /// <summary>
        /// 연결 카드 두 번째 줄에 보여 줄 요약 문자열을 다시 만듭니다.
        /// </summary>
        private void RefreshConnectionMeta(ConnectionModel conn)
        {
            if (conn == null) return;

            var parts = new List<string>();

            if (conn.Type == "Server" && !string.IsNullOrEmpty(conn.ResponsePattern))
            {
                parts.Add(DescribeResponsePattern(conn.ResponsePattern));
            }

            parts.Add(conn.EncodingName);

            if (conn.IsPeriodicSending) parts.Add($"주기전송 {IntervalText}ms");
            if (conn.IsForwardingEnabled) parts.Add($"전달 → {conn.ForwardIpAddress}:{conn.ForwardPort}");

            conn.MetaText = string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        /// <summary>
        /// 응답 패턴 코드값을 카드에 보여 줄 짧은 이름으로 바꿉니다.
        /// </summary>
        private static string DescribeResponsePattern(string pattern)
        {
            switch (pattern)
            {
                case "Echo": return "Echo";
                case "SendOnce": return "Send Once";
                case "ReplyAfterReceive": return "Reply";
                case "ListenOnly": return "Listen Only";
                default: return pattern;
            }
        }

        #endregion

        #region Banner (인라인 알림)

        /// <summary>
        /// 알림 배너를 목록 맨 위에 추가합니다.
        /// 같은 연결의 같은 종류 배너가 이미 있으면 새 것으로 교체합니다.
        /// </summary>
        private void ShowBanner(BannerItem banner)
        {
            if (banner == null) return;

            banner.DismissCommand = DismissBannerCommand;

            var duplicate = Banners.FirstOrDefault(b => b.Kind == banner.Kind && b.ConnectionId == banner.ConnectionId);
            if (duplicate != null) Banners.Remove(duplicate);

            Banners.Insert(0, banner);

            // 화면을 덮지 않도록 최대 3건만 유지합니다.
            while (Banners.Count > 3) Banners.RemoveAt(Banners.Count - 1);
        }

        /// <summary>
        /// 배너 한 건을 닫습니다.
        /// </summary>
        private void DismissBanner(BannerItem banner)
        {
            if (banner != null) Banners.Remove(banner);
        }

        /// <summary>
        /// 특정 연결에 대해 남아 있는 배너를 모두 없앱니다. (재시작에 성공했을 때 등)
        /// </summary>
        private void ClearBannersFor(ConnectionModel conn)
        {
            if (conn == null) return;
            foreach (var stale in Banners.Where(b => b.ConnectionId == conn.Id).ToList())
            {
                Banners.Remove(stale);
            }
        }

        /// <summary>
        /// 서버 바인딩 실패를 인라인 배너로 알립니다. (목업 1f)
        /// </summary>
        private void OnServerStartFailed(ConnectionModel conn, string message, string technical, SocketError error)
        {
            conn.ErrorText = message;

            var banner = new BannerItem
            {
                Severity = BannerSeverity.Error,
                Kind = "bind-failed",
                ConnectionId = conn.Id,
                Title = $"포트 {conn.Port}을(를) 열 수 없습니다 — Cannot bind {conn.IpAddress}:{conn.Port}",
                Detail = error == SocketError.AddressAlreadyInUse
                    ? "다른 프로세스가 이미 이 포트를 사용 중입니다."
                    : message,
                TechnicalDetail = technical,
                PrimaryActionText = "다시 시도 Retry",
                SecondaryActionText = "포트 변경 Change port"
            };

            banner.PrimaryActionCommand = new RelayCommand(_ =>
            {
                DismissBanner(banner);
                conn.ErrorText = null;
                StartConnection(conn);
            });

            banner.SecondaryActionCommand = new RelayCommand(_ =>
            {
                DismissBanner(banner);
                SelectedConnection = conn;
                SelectedItems.Clear();
                SelectedItems.Add(conn);
                ExecuteEdit(null);
            });

            // 포트 점유 프로세스 조회는 이 오류에서만 의미가 있습니다.
            if (error == SocketError.AddressAlreadyInUse)
            {
                banner.TertiaryActionText = "누가 쓰는지 보기";
                banner.TertiaryActionCommand = new RelayCommand(async _ =>
                {
                    banner.StatusNote = "조회 중...";
                    banner.StatusNote = await PortOwnerLookup.DescribeOwnerAsync(conn.Port);
                });
            }

            ShowBanner(banner);
        }

        /// <summary>
        /// 클라이언트 접속 실패나 연결 끊김을 인라인 배너로 알립니다. (목업 1f)
        /// </summary>
        private void OnClientConnectionFailed(ConnectionModel conn, string message, bool isConnectFailure)
        {
            var banner = new BannerItem
            {
                Severity = isConnectFailure ? BannerSeverity.Error : BannerSeverity.Warning,
                Kind = isConnectFailure ? "connect-failed" : "peer-closed",
                ConnectionId = conn.Id,
                Title = isConnectFailure
                    ? $"접속할 수 없습니다 — {conn.IpAddress}:{conn.Port}"
                    : $"연결이 끊어졌습니다 — {conn.IpAddress}:{conn.Port}",
                Detail = isConnectFailure
                    ? message
                    : $"{message} at {DateTime.Now:HH:mm:ss.fff}" + (conn.IsPeriodicSending ? " · 주기 전송이 중단되었습니다" : ""),
                PrimaryActionText = isConnectFailure ? "다시 시도 Retry" : "재연결 Reconnect"
            };

            if (isConnectFailure) conn.ErrorText = message;

            banner.PrimaryActionCommand = new RelayCommand(_ =>
            {
                DismissBanner(banner);
                conn.ErrorText = null;
                StartConnection(conn);
            });

            ShowBanner(banner);
        }

        #endregion

        #region 최근 세션 / 제어 문자

        /// <summary>
        /// 최근 세션 파일 목록을 디스크에서 다시 읽어 옵니다.
        /// </summary>
        private void RefreshRecentSessions()
        {
            RecentSessionPaths.Clear();
            foreach (var path in RecentSessionService.Load()) RecentSessionPaths.Add(path);
        }

        /// <summary>
        /// 빈 상태 화면의 '최근:' 항목을 눌렀을 때 해당 세션 파일을 불러옵니다.
        /// </summary>
        private void ExecuteOpenRecentSession(object param)
        {
            string path = param as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            LoadSessionFile(path);
        }

        /// <summary>
        /// 메뉴에서 테마를 골랐을 때 호출됩니다.
        /// </summary>
        private void ExecuteSetTheme(object param)
        {
            if (Enum.TryParse(param as string, out AppTheme theme))
            {
                ThemeService.Apply(theme);
            }
        }

        /// <summary>
        /// 테마 메뉴 세 항목의 체크 상태를 다시 계산하도록 알립니다.
        /// </summary>
        private void RaiseThemeSelectionChanged()
        {
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
        }

        /// <summary>
        /// 제어 문자 칩을 눌렀을 때, 현재 활성화된 입력 상자 끝에 태그를 덧붙입니다.
        /// </summary>
        private void ExecuteInsertControlCharacter(object param)
        {
            string tag = param as string;
            if (string.IsNullOrEmpty(tag)) return;

            if (IsPeriodicMode) PeriodicSendText = (PeriodicSendText ?? string.Empty) + tag;
            else SendText = (SendText ?? string.Empty) + tag;
        }

        #endregion

        #region Helper Methods (계속)

        /// <summary>
        /// 상태 표시줄의 연결 통계 정보를 업데이트합니다.
        /// </summary>
        private void UpdateConnectionStats()
        {
            int total = Connections.Count;
            int activeServers = Connections.Count(c => c.Type == "Server" && c.Status == "Listening");
            int activeClients = Connections.Count(c => c.Type == "Client" && c.Status == "Connected");
            ConnectionStatsText = $"Total: {total} (Servers: {activeServers}, Clients: {activeClients})";
            OnPropertyChanged(nameof(ConnectionStatsText));
        }

        #endregion
    }

    /// <summary>
    /// '제어 문자 삽입' ContextMenu의 각 메뉴 항목을 정의하는 데이터 모델 클래스입니다.
    /// </summary>
    public class ControlCharacterItem
    {
        /// <summary>
        /// UI의 메뉴에 표시될 텍스트입니다. (예: "[STX] Start of Text")
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 메뉴 항목을 클릭했을 때 텍스트 박스에 실제 삽입될 태그 문자열입니다. (예: "[STX]")
        /// </summary>
        public string Tag { get; set; }

        /// <summary>
        /// 컴포저의 칩 버튼에 표시될 짧은 이름입니다. (예: "STX")
        /// </summary>
        public string ShortName => Tag != null ? Tag.Trim('[', ']') : string.Empty;
    }
}