using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SocketTestTool.Models;
using SocketTestTool.Services;
using SocketTestTool.ViewModels;
using SocketTestTool.Views;
using static FullQa.Qa;

namespace FullQa
{
    /// <summary>
    /// SocketTestTool이 제공하는 모든 기능을 실제 앱·실제 소켓으로 훑는 QA 스위트입니다.
    /// </summary>
    internal static class Suite
    {
        private static SocketTestTool.MainWindow _w;
        private static MainViewModel _vm;

        [STAThread]
        private static int Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("pack://application:,,,/SocketTestTool;component/Themes/Light.xaml", UriKind.Absolute) });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("pack://application:,,,/SocketTestTool;component/Themes/Fluent.xaml", UriKind.Absolute) });
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            _w = new SocketTestTool.MainWindow
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = 20, Top = 20, Topmost = true };
            _w.Show();
            _w.Activate();
            Pump(500);
            _vm = (MainViewModel)_w.DataContext;

            var sections = new (string, Action)[]
            {
                ("A. 버전 표기", VersionInfo),
                ("B. 빈 상태 화면", EmptyState),
                ("C. 연결 추가 대화상자", AddDialog),
                ("D. 연결 목록 관리", ConnectionList),
                ("E. 서버 응답 패턴", ResponsePatterns),
                ("F. 규칙 기반 응답", Rules),
                ("G. 수신 조각 합치기", Fragmentation),
                ("H. 전송 기능", Sending),
                ("I. 인코딩", Encodings),
                ("J. 로그 기능", Logging),
                ("K. 파일 로깅 / 로그 저장", FileLogging),
                ("L. 수신 데이터 자동 전달", Forwarding),
                ("M. 실패 처리와 배너", Failures),
                ("M2. 클라이언트 자동 응답", ClientAutoReply),
                ("N. 세션 저장 / 불러오기", Session),
                ("O. 테마", Theme),
                ("P. 자원 정리 / 경합", Cleanup),
            };

            foreach (var (title, run) in sections)
            {
                Section(title);
                try { run(); }
                catch (Exception ex)
                {
                    Check("구간이 끝까지 실행됨", false, ex.GetType().Name + ": " + ex.Message);
                    Note((ex.StackTrace ?? "").Split('\n').FirstOrDefault() ?? "");
                }
                Reset();
            }

            Write(Path.Combine(Path.GetTempPath(), "fullqa-result.txt"));
            return FailCount == 0 ? 0 : 1;
        }

        #region Helpers

        private static void Reset()
        {
            foreach (var c in _vm.Connections.ToList())
            {
                _vm.SelectedItems.Clear();
                _vm.SelectedItems.Add(c);
                _vm.StopConnectionCommand.Execute(null);
            }
            Pump(200);
            _vm.Connections.Clear();
            _vm.Banners.Clear();
            _vm.SelectedItems.Clear();
            _vm.SelectedConnection = null;
            _vm.SearchText = "";
            _vm.IsPeriodicMode = false;
            Pump(120);
        }

        private static ConnectionModel Server(int port, string pattern = "Echo", string reply = null,
                                              bool endless = false, string enc = "ASCII", int timeout = 100)
            => new ConnectionModel
            {
                Type = "Server", IpAddress = "127.0.0.1", Port = port, Address = "127.0.0.1:" + port,
                Status = "Stopped", ResponsePattern = pattern, ReplyMessage = reply, IsReplyEndless = endless,
                ReceiveTimeout = timeout, EncodingName = enc
            };

        private static ConnectionModel Client(int port, string enc = "ASCII")
            => new ConnectionModel
            {
                Type = "Client", IpAddress = "127.0.0.1", Port = port, Address = "127.0.0.1:" + port,
                Status = "Stopped", EncodingName = enc
            };

        private static void Select(params ConnectionModel[] conns)
        {
            _vm.SelectedConnection = conns.FirstOrDefault();
            _vm.SelectedItems.Clear();
            foreach (var c in conns) _vm.SelectedItems.Add(c);
            Pump(40);
        }

        private static ConnectionModel Add(ConnectionModel c) { _vm.Connections.Add(c); Pump(40); return c; }

        private static void Start(params ConnectionModel[] c) { Select(c); _vm.StartConnectionCommand.Execute(null); }
        private static void Stop(params ConnectionModel[] c) { Select(c); _vm.StopConnectionCommand.Execute(null); Pump(150); }

        private static bool Logged(ConnectionModel c, LogDirection d, string contains)
            => c.Logs.Any(l => l.Direction == d &&
                               ((l.Message ?? "").Contains(contains) || (l.DecodedData ?? "").Contains(contains)));

        private static ConnectionModel RunningServer(out int port, string pattern = "Echo", string reply = null,
                                                     bool endless = false, string enc = "ASCII", int timeout = 100)
        {
            port = FreePort();
            var s = Add(Server(port, pattern, reply, endless, enc, timeout));
            Start(s);
            WaitUntil(() => s.Status == "Listening", 3000);
            return s;
        }

        #endregion

        #region A. 버전

        private static void VersionInfo()
        {
            var asm = typeof(MainViewModel).Assembly.GetName().Version;
            Check("어셈블리 버전이 2.x", asm != null && asm.Major == 2, asm?.ToString());
            Check("상태 표시줄 버전이 어셈블리에서 자동으로 나옴",
                  _vm.AppVersionText == $"v{asm.Major}.{asm.Minor}.{asm.Build}", _vm.AppVersionText);

            var latest = ReleaseHistory.All.First();
            Check("릴리스 기록의 최신 항목이 어셈블리 버전과 일치",
                  latest.Version == _vm.AppVersionText, $"note={latest.Version} asm={_vm.AppVersionText}");
            Check("최신 항목에만 CURRENT 표시",
                  ReleaseHistory.All.Count(r => r.IsCurrent) == 1 && latest.IsCurrent);
            // 버전 목록을 하드코딩하면 릴리스가 늘 때마다 이 검사가 깨집니다.
            // 목록의 '내용'이 아니라 '내림차순으로 정렬돼 있는지'라는 규칙 자체를 검증합니다.
            var parsed = ReleaseHistory.All
                .Select(r => Version.Parse(r.Version.TrimStart('v')))
                .ToList();
            Check("버전이 내림차순으로 정렬됨",
                  parsed.SequenceEqual(parsed.OrderByDescending(v => v)),
                  string.Join(",", ReleaseHistory.All.Select(r => r.Version)));
            Check("버전에 중복이 없음",
                  parsed.Distinct().Count() == parsed.Count,
                  string.Join(",", ReleaseHistory.All.Select(r => r.Version)));
            Check("VERSIONING.md 문서가 있음",
                  File.Exists(Path.Combine(RepoRoot(), "VERSIONING.md")), RepoRoot());

            // 버전 기록 창이 실제로 열리고 데이터가 붙는지
            var vh = new VersionHistoryWindow
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
            vh.Show(); vh.UpdateLayout(); Pump(200);
            Check("버전 기록 창이 최신 버전을 펼쳐 보여 줌", (vh.DataContext as ReleaseNote)?.Version == latest.Version,
                  (vh.DataContext as ReleaseNote)?.Version);
            var list = Find<ListBox>(vh);
            // 개수를 고정하지 않고 '릴리스 기록 전부가 목록에 나오는지'를 봅니다.
            Check("좌측 버전 목록에 릴리스 기록 전부가 나옴",
                  list != null && list.Items.Count == ReleaseHistory.All.Count,
                  $"목록 {list?.Items.Count} / 기록 {ReleaseHistory.All.Count}");
            vh.Close();
        }

        #endregion

        #region B. 빈 상태

        private static void EmptyState()
        {
            Check("연결이 없으면 목록이 비어 있음", _vm.Connections.Count == 0);

            var addServer = Find<Button>(_w, b => (b.Content as string) == "서버 추가 Add Server");
            var addClient = Find<Button>(_w, b => (b.Content as string) == "클라이언트 추가 Add Client");
            var load = Find<Button>(_w, b => (b.Content as string) == "세션 불러오기 Load session…");

            Check("빈 상태 화면에 '서버 추가' 진입점", addServer != null && addServer.IsVisible);
            Check("빈 상태 화면에 '클라이언트 추가' 진입점", addClient != null && addClient.IsVisible);
            Check("빈 상태 화면에 '세션 불러오기' 진입점", load != null && load.IsVisible);
            Check("진입점 버튼에 커맨드가 연결됨",
                  addServer?.Command != null && addClient?.Command != null && load?.Command != null);

            CaptureScreen("empty-state");

            // 연결이 생기면 빈 상태가 사라지는지
            Add(Server(FreePort(), "ListenOnly"));
            Pump(200);
            Check("연결을 만들면 빈 상태 화면이 사라짐",
                  Find<Button>(_w, b => (b.Content as string) == "서버 추가 Add Server") == null ||
                  !Find<Button>(_w, b => (b.Content as string) == "서버 추가 Add Server").IsVisible);
        }

        #endregion

        #region C. 연결 추가 대화상자

        private static void AddDialog()
        {
            // --- 서버 모드로 새 연결 ---
            var dlg = new AddConnectionWindow(true, null, _vm.Connections)
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
            dlg.Show(); dlg.UpdateLayout(); Pump(150);

            Check("서버 모드 기본 IP가 0.0.0.0", Field<TextBox>(dlg, "IpTextBox")?.Text == "0.0.0.0",
                  Field<TextBox>(dlg, "IpTextBox")?.Text);
            Check("서버 모드에서 응답 설정이 보임",
                  Field<StackPanel>(dlg, "ResponseOptionsPanel")?.Visibility == Visibility.Visible);
            Check("서버 모드에서는 Echo·접속 시 1회 전송 카드가 보임",
                  Field<RadioButton>(dlg, "EchoRadio")?.Visibility == Visibility.Visible &&
                  Field<RadioButton>(dlg, "SendOnceRadio")?.Visibility == Visibility.Visible);
            Check("기본 응답 패턴이 Echo", Field<RadioButton>(dlg, "EchoRadio")?.IsChecked == true);
            Check("기본 인코딩이 ASCII", dlg.EncodingName == "ASCII", dlg.EncodingName);

            // 값 채우고 확정
            Field<TextBox>(dlg, "IpTextBox").Text = "127.0.0.1";
            Field<TextBox>(dlg, "PortTextBox").Text = "18080";
            Field<RadioButton>(dlg, "ReplyAfterReceiveRadio").IsChecked = true;
            Pump(80);
            Check("'수신 후 응답' 선택 시 전용 옵션이 나타남",
                  Field<StackPanel>(dlg, "ReplyOptionsPanel")?.Visibility == Visibility.Visible);

            Field<TextBox>(dlg, "ReplyMessageTextBox").Text = "[STX]ACK[ETX]";
            Field<TextBox>(dlg, "ReceiveTimeoutTextBox").Text = "250";
            Field<CheckBox>(dlg, "EndlessReplyCheckBox").IsChecked = true;

            // 규칙 추가
            Field<TextBox>(dlg, "ReceiveRuleTextBox").Text = "PING";
            Field<TextBox>(dlg, "SendRuleTextBox").Text = "PONG";
            Invoke(dlg, "AddRule_Click", null, null);
            Pump(80);
            Check("규칙이 표에 추가됨", dlg.Rules.Count == 1 && dlg.Rules[0].SendData == "PONG");
            Check("규칙 개수 표시가 갱신됨", Field<TextBlock>(dlg, "RuleCountText")?.Text == "1 rule",
                  Field<TextBlock>(dlg, "RuleCountText")?.Text);

            // 자동 전달
            Field<CheckBox>(dlg, "ForwardingCheckBox").IsChecked = true;
            Field<TextBox>(dlg, "ForwardIpTextBox").Text = "127.0.0.1";
            Field<TextBox>(dlg, "ForwardPortTextBox").Text = "19090";
            Pump(80);
            Check("전달을 켜면 대상 입력란이 나타남",
                  Field<Grid>(dlg, "ForwardTargetPanel")?.Visibility == Visibility.Visible);

            Invoke(dlg, "OkButton_Click", null, null);

            Check("대화상자 결과: IP/Port", dlg.IpAddress == "127.0.0.1" && dlg.Port == 18080);
            Check("대화상자 결과: 응답 패턴", dlg.ResponsePattern == "ReplyAfterReceive", dlg.ResponsePattern);
            Check("대화상자 결과: 응답 메시지/지속/타임아웃",
                  dlg.ReplyMessage == "[STX]ACK[ETX]" && dlg.IsReplyEndless && dlg.ReceiveTimeout == 250,
                  $"{dlg.ReplyMessage}/{dlg.IsReplyEndless}/{dlg.ReceiveTimeout}");
            Check("대화상자 결과: 자동 전달",
                  dlg.IsForwardingEnabled && dlg.ForwardIpAddress == "127.0.0.1" && dlg.ForwardPort == 19090);
            Check("대화상자 결과: 서버 모드", dlg.IsServerMode);
            dlg.Close();

            // --- 클라이언트 모드 ---
            var dlg2 = new AddConnectionWindow(false, null, _vm.Connections)
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
            dlg2.Show(); dlg2.UpdateLayout(); Pump(150);
            Check("클라이언트 모드 기본 IP가 127.0.0.1", Field<TextBox>(dlg2, "IpTextBox")?.Text == "127.0.0.1");
            Check("클라이언트 모드에도 응답 설정이 보임",
                  Field<StackPanel>(dlg2, "ResponseOptionsPanel")?.Visibility == Visibility.Visible);
            Check("클라이언트 모드에서는 Echo·접속 시 1회 전송 카드가 숨겨짐",
                  Field<RadioButton>(dlg2, "EchoRadio")?.Visibility == Visibility.Collapsed &&
                  Field<RadioButton>(dlg2, "SendOnceRadio")?.Visibility == Visibility.Collapsed);
            Check("클라이언트 기본값은 자동 응답 없음",
                  Field<RadioButton>(dlg2, "ListenOnlyRadio")?.IsChecked == true);

            // 모드 전환
            Field<RadioButton>(dlg2, "ServerModeRadio").IsChecked = true;
            Pump(120);
            Check("대화상자 안에서 서버로 전환 가능",
                  dlg2.IsServerMode && Field<RadioButton>(dlg2, "EchoRadio")?.Visibility == Visibility.Visible);
            Check("모드 전환 시 기본 IP도 함께 바뀜", Field<TextBox>(dlg2, "IpTextBox")?.Text == "0.0.0.0");
            dlg2.Close();

            // --- 검증: 잘못된 전달 대상 ---
            var dlg3 = new AddConnectionWindow(true, null, _vm.Connections)
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
            dlg3.Show(); dlg3.UpdateLayout(); Pump(120);
            Field<TextBox>(dlg3, "PortTextBox").Text = "18081";
            Field<CheckBox>(dlg3, "ForwardingCheckBox").IsChecked = true;
            Field<TextBox>(dlg3, "ForwardIpTextBox").Text = "안녕하세요";
            Invoke(dlg3, "OkButton_Click", null, null);
            Check("잘못된 전달 대상 IP를 막고 창 안에서 알림",
                  (Field<TextBlock>(dlg3, "StatusText")?.Text ?? "").Contains("올바르지 않습니다"),
                  Field<TextBlock>(dlg3, "StatusText")?.Text);

            // 자기 자신에게 전달
            Field<TextBox>(dlg3, "ForwardIpTextBox").Text = "127.0.0.1";
            Field<TextBox>(dlg3, "ForwardPortTextBox").Text = "18081";
            Field<TextBox>(dlg3, "IpTextBox").Text = "127.0.0.1";
            Invoke(dlg3, "OkButton_Click", null, null);
            Check("자기 자신에게 전달하는 설정을 막음",
                  (Field<TextBlock>(dlg3, "StatusText")?.Text ?? "").Contains("이 서버 자신"),
                  Field<TextBlock>(dlg3, "StatusText")?.Text);
            dlg3.Close();

            // --- 수정 모드 ---
            var existing = Server(18082, "SendOnce", "WELCOME");
            existing.Rules.Add(new ResponseRule { ReceiveData = "A", SendData = "B" });
            existing.IsForwardingEnabled = true;
            existing.ForwardIpAddress = "10.0.0.9";
            existing.ForwardPort = 7777;
            var dlg4 = new AddConnectionWindow(true, existing, _vm.Connections)
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
            dlg4.Show(); dlg4.UpdateLayout(); Pump(150);

            Check("수정 모드: 제목이 바뀜", dlg4.Title.Contains("수정"), dlg4.Title);
            Check("수정 모드: 종류를 바꿀 수 없음",
                  Field<RadioButton>(dlg4, "ServerModeRadio")?.IsEnabled == false);
            Check("수정 모드: 기존 값이 채워짐",
                  Field<TextBox>(dlg4, "PortTextBox")?.Text == "18082" &&
                  Field<RadioButton>(dlg4, "SendOnceRadio")?.IsChecked == true &&
                  dlg4.Rules.Count == 1 &&
                  Field<TextBox>(dlg4, "ForwardIpTextBox")?.Text == "10.0.0.9");
            dlg4.Close();

            // --- 확인 결과 문구가 길어도 잘리지 않아야 합니다 ---
            // 예전에는 가로 StackPanel 안에 있어 자식이 무한 너비를 받았고,
            // 그래서 TextWrapping="Wrap"이 동작하지 않아 긴 점유 프로세스 문구가 잘렸습니다.
            var dlg5 = new AddConnectionWindow(true, null, _vm.Connections)
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
            dlg5.Show(); dlg5.UpdateLayout(); Pump(150);

            var resultPanel = Field<Border>(dlg5, "CheckResultPanel");
            var resultText = Field<TextBlock>(dlg5, "StatusText");

            Invoke(dlg5, "ShowCheckResult", true, "TCP 18080 free — 포트 사용 가능합니다.");
            dlg5.UpdateLayout(); Pump(150);
            double oneLineHeight = resultText?.ActualHeight ?? 0;

            // 한 줄에 절대 들어가지 않는 길이라야 예전 버그(무한 너비 -> 잘림)를 실제로 재현합니다.
            string longMsg = "TCP 18080 busy — 사용 중: VeryLongBackgroundServiceProcessName.exe (PID 123456) — " +
                             "포트를 다른 프로세스가 이미 점유하고 있어 이 주소로는 서버를 열 수 없습니다.";
            Invoke(dlg5, "ShowCheckResult", false, longMsg);
            dlg5.UpdateLayout(); Pump(150);

            Check("확인 결과 영역이 표시됨", resultPanel?.Visibility == Visibility.Visible);
            Check("긴 문구가 잘리지 않고 문자열 그대로 유지됨", resultText?.Text == longMsg);
            Check("긴 문구가 패널 너비를 넘지 않음",
                  resultText != null && resultPanel != null && resultText.ActualWidth <= resultPanel.ActualWidth,
                  $"글자={resultText?.ActualWidth:0}px 패널={resultPanel?.ActualWidth:0}px");
            Check("긴 문구가 여러 줄로 접힘",
                  resultText != null && oneLineHeight > 0 && resultText.ActualHeight > oneLineHeight,
                  $"한 줄={oneLineHeight:0}px 접힘={resultText?.ActualHeight:0}px");

            // 실제로 쓰이는 문구(중복 제거 후)는 한 줄에 들어가야 합니다.
            Invoke(dlg5, "ShowCheckResult", false, "TCP 18080 busy — 사용 중: nginx.exe (PID 4812)");
            dlg5.UpdateLayout(); Pump(150);
            Check("실제 점유 문구는 한 줄에 들어감",
                  resultText != null && resultText.ActualHeight <= oneLineHeight,
                  $"높이={resultText?.ActualHeight:0}px 한 줄={oneLineHeight:0}px");
            dlg5.Close();
        }

        #endregion

        #region D. 연결 목록 관리

        private static void ConnectionList()
        {
            var a = Add(Server(FreePort(), "ListenOnly"));
            var b = Add(Client(FreePort()));
            var c = Add(Server(FreePort(), "Echo"));

            Check("Seq가 1부터 순서대로 매겨짐",
                  a.Seq == 1 && b.Seq == 2 && c.Seq == 3, $"{a.Seq},{b.Seq},{c.Seq}");
            Check("상태 표시줄 총 개수", _vm.ConnectionStatsText.Contains("Total: 3"), _vm.ConnectionStatsText);

            // 중간 항목 제거 → Seq 재정렬
            Select(b);
            _vm.RemoveCommand.Execute(null);
            Pump(150);
            Check("Remove가 목록에서 제거", _vm.Connections.Count == 2);
            Check("제거 후 Seq가 다시 매겨짐", _vm.Connections[0].Seq == 1 && _vm.Connections[1].Seq == 2);

            // 다중 선택 일괄 시작
            Start(_vm.Connections.ToArray());
            Check("다중 선택으로 한꺼번에 시작",
                  WaitUntil(() => _vm.Connections.All(x => x.Status == "Listening"), 4000),
                  string.Join(",", _vm.Connections.Select(x => x.Status)));
            Check("상태 표시줄에 활성 서버 수 반영",
                  _vm.ConnectionStatsText.Contains("Servers: 2"), _vm.ConnectionStatsText);

            // 실행 중에는 Start가 비활성
            Check("실행 중인 연결은 Start 불가", !_vm.StartConnectionCommand.CanExecute(null));
            Check("실행 중인 연결은 Stop 가능", _vm.StopConnectionCommand.CanExecute(null));

            Stop(_vm.Connections.ToArray());
            Check("다중 선택으로 한꺼번에 중지",
                  WaitUntil(() => _vm.Connections.All(x => x.Status == "Stopped"), 3000),
                  string.Join(",", _vm.Connections.Select(x => x.Status)));

            // 중복 서버 방지
            int dup = FreePort();
            Add(Server(dup, "ListenOnly"));
            var dlg = new AddConnectionWindow(true, null, _vm.Connections)
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
            dlg.Show(); Pump(100);
            Field<TextBox>(dlg, "IpTextBox").Text = "127.0.0.1";
            Field<TextBox>(dlg, "PortTextBox").Text = dup.ToString();
            Invoke(dlg, "OkButton_Click", null, null);
            dlg.Close();

            int before = _vm.Connections.Count;
            Invoke(_vm, "AddConnectionFromDialog", dlg);
            Pump(120);
            Check("같은 IP/Port 서버 중복 추가를 막음", _vm.Connections.Count == before, $"{before} -> {_vm.Connections.Count}");
            Check("중복 시 인라인 배너로 알림", _vm.Banners.Any(x => x.Kind == "duplicate-server"),
                  string.Join(",", _vm.Banners.Select(x => x.Kind)));
        }

        #endregion

        #region E. 응답 패턴

        private static void ResponsePatterns()
        {
            // Echo
            var s = RunningServer(out int p1, "Echo");
            using (var raw = new RawClient())
            {
                Check("Echo: 접속", raw.Connect("127.0.0.1", p1));
                raw.SendAscii("ECHO-ME");
                Check("Echo: 받은 그대로 되돌려 보냄",
                      WaitUntil(() => raw.TextAscii() == "ECHO-ME", 3000), raw.TextAscii());
            }
            Stop(s);

            // SendOnce
            var s2 = RunningServer(out int p2, "SendOnce", "WELCOME");
            using (var raw = new RawClient())
            {
                raw.Connect("127.0.0.1", p2);
                Check("SendOnce: 접속 즉시 1회 전송",
                      WaitUntil(() => raw.TextAscii() == "WELCOME", 3000), raw.TextAscii());
                raw.Clear();
                raw.SendAscii("X");
                Pump(600);
                Check("SendOnce: 이후 자동 응답 없음", raw.Count == 0, raw.TextAscii());
            }
            Stop(s2);

            // ReplyAfterReceive 1회
            var s3 = RunningServer(out int p3, "ReplyAfterReceive", "ACK1");
            using (var raw = new RawClient())
            {
                raw.Connect("127.0.0.1", p3);
                raw.SendAscii("A");
                WaitUntil(() => raw.TextAscii() == "ACK1", 3000);
                raw.SendAscii("B");
                Pump(700);
                Check("ReplyAfterReceive(1회): 첫 수신에만 응답", raw.TextAscii() == "ACK1", raw.TextAscii());
            }
            Stop(s3);

            // ReplyAfterReceive 지속
            var s4 = RunningServer(out int p4, "ReplyAfterReceive", "ACK", endless: true);
            using (var raw = new RawClient())
            {
                raw.Connect("127.0.0.1", p4);
                raw.SendAscii("A");
                WaitUntil(() => raw.TextAscii() == "ACK", 3000);
                raw.SendAscii("B");
                Check("ReplyAfterReceive(지속): 매번 응답",
                      WaitUntil(() => raw.TextAscii() == "ACKACK", 3000), raw.TextAscii());
            }
            Stop(s4);

            // ListenOnly
            var s5 = RunningServer(out int p5, "ListenOnly");
            using (var raw = new RawClient())
            {
                raw.Connect("127.0.0.1", p5);
                raw.SendAscii("NO-REPLY");
                Pump(700);
                Check("ListenOnly: 자동 응답 없음", raw.Count == 0, raw.TextAscii());
                Check("ListenOnly: 수신은 기록됨", Logged(s5, LogDirection.Received, "NO-REPLY"));
            }
            Stop(s5);
        }

        #endregion

        #region F. 규칙

        private static void Rules()
        {
            int port = FreePort();
            var s = Server(port, "Echo");
            s.Rules.Add(new ResponseRule { ReceiveData = "PING", SendData = "PONG" });
            s.Rules.Add(new ResponseRule { ReceiveData = "ORD|", SendData = "ACK-ORD" });
            Add(s);
            Start(s);
            WaitUntil(() => s.Status == "Listening", 3000);

            using (var raw = new RawClient())
            {
                raw.Connect("127.0.0.1", port);

                raw.SendAscii("PING");
                Check("규칙 1이 동작", WaitUntil(() => raw.TextAscii() == "PONG", 3000), raw.TextAscii());

                raw.Clear();
                raw.SendAscii("ORD|00417");
                Check("규칙 2가 동작", WaitUntil(() => raw.TextAscii() == "ACK-ORD", 3000), raw.TextAscii());

                raw.Clear();
                raw.SendAscii("OTHER");
                Check("규칙에 없으면 Echo로 처리 (규칙이 Echo보다 우선)",
                      WaitUntil(() => raw.TextAscii() == "OTHER", 3000), raw.TextAscii());

                Check("규칙 응답이 로그에 남음", Logged(s, LogDirection.Sent, "PONG"));
            }
        }

        #endregion

        #region G. 조각 합치기

        private static void Fragmentation()
        {
            int port = FreePort();
            var s = Add(Server(port, "ListenOnly", timeout: 400));
            Start(s);
            WaitUntil(() => s.Status == "Listening", 3000);

            using (var raw = new RawClient())
            {
                raw.Connect("127.0.0.1", port);

                // 400ms 침묵 이내로 쪼개 보냅니다 -> 한 건으로 합쳐져야 합니다.
                raw.SendAscii("AB");
                Pump(80);
                raw.SendAscii("CD");
                Pump(80);
                raw.SendAscii("EF");

                bool merged = WaitUntil(() => s.Logs.Any(l => l.Direction == LogDirection.Received &&
                                                              (l.DecodedData ?? "") == "ABCDEF"), 4000);
                var got = s.Logs.Where(l => l.Direction == LogDirection.Received).Select(l => l.DecodedData).ToList();
                Check("ReceiveTimeout 이내로 끊겨 온 조각이 한 건으로 합쳐짐", merged, "[" + string.Join(" | ", got) + "]");
                Check("합쳐진 결과가 1건", got.Count == 1, "count=" + got.Count);

                // 침묵 이후는 별도 건
                Pump(700);
                raw.SendAscii("GH");
                Check("침묵 이후 도착분은 별도 메시지",
                      WaitUntil(() => s.Logs.Count(l => l.Direction == LogDirection.Received) == 2, 4000),
                      "count=" + s.Logs.Count(l => l.Direction == LogDirection.Received));
            }
        }

        #endregion

        #region H. 전송

        private static void Sending()
        {
            var s = RunningServer(out int port, "ListenOnly");
            var c = Add(Client(port));
            Start(c);
            WaitUntil(() => c.Status == "Connected", 3000);

            // 1회 전송
            Select(c);
            _vm.SendText = "ONCE";
            Check("연결 중일 때만 전송 가능", _vm.SendCommand.CanExecute(null));
            _vm.SendCommand.Execute(null);
            Check("1회 전송이 서버에 도착", WaitUntil(() => Logged(s, LogDirection.Received, "ONCE"), 3000));
            Check("보낸 내용이 Sent 로그에 남음", Logged(c, LogDirection.Sent, "ONCE"));
            Check("송신 바이트 카운터", WaitUntil(() => c.BytesSent == 4, 2000), "sent=" + c.BytesSent);

            // 제어문자 칩
            _vm.SendText = "";
            _vm.InsertControlCharacterCommand.Execute("[STX]");
            _vm.SendText += "ACK";
            _vm.InsertControlCharacterCommand.Execute("[ETX]");
            Check("제어문자 칩이 태그를 삽입", _vm.SendText == "[STX]ACK[ETX]", _vm.SendText);
            Check("바이트 카운터가 태그를 실제 바이트로 계산", _vm.SendTextByteCount == 5, "" + _vm.SendTextByteCount);
            _vm.SendCommand.Execute(null);
            Check("제어문자가 실제 바이트로 전송됨 (02 41 43 4B 03)",
                  WaitUntil(() => s.Logs.Any(l => l.Direction == LogDirection.Received && Hex(l.Data) == "02 41 43 4B 03"), 3000),
                  Hex(s.Logs.LastOrDefault(l => l.Direction == LogDirection.Received)?.Data));

            // 주기 전송
            _vm.IsPeriodicMode = true;
            _vm.PeriodicSendText = "HB";
            _vm.IntervalText = "200";
            int before = s.Logs.Count(l => l.Direction == LogDirection.Received);
            _vm.PeriodicSendCommand.Execute(null);
            Check("주기 전송 시작", c.IsPeriodicSending);
            Check("카드 요약에 주기 전송 표시", WaitUntil(() => (c.MetaText ?? "").Contains("주기전송 200ms"), 1500), c.MetaText);
            Check("주기 전송이 반복 도착",
                  WaitUntil(() => s.Logs.Count(l => l.Direction == LogDirection.Received) >= before + 3, 4000),
                  "count=" + (s.Logs.Count(l => l.Direction == LogDirection.Received) - before));
            _vm.PeriodicSendCommand.Execute(null);
            Check("주기 전송 중지", !c.IsPeriodicSending);
            int after = s.Logs.Count(l => l.Direction == LogDirection.Received);
            Pump(700);
            Check("중지 후 더 오지 않음", s.Logs.Count(l => l.Direction == LogDirection.Received) <= after + 1);
            _vm.IsPeriodicMode = false;

            // 서버 -> 전 클라이언트 브로드캐스트
            Stop(c);
            var bs = RunningServer(out int bport, "ListenOnly");
            using (var r1 = new RawClient())
            using (var r2 = new RawClient())
            {
                r1.Connect("127.0.0.1", bport);
                r2.Connect("127.0.0.1", bport);
                Pump(300);

                Select(bs);
                _vm.SendText = "BROADCAST";
                _vm.SendCommand.Execute(null);

                Check("서버 전송이 접속한 모든 클라이언트에 도착",
                      WaitUntil(() => r1.TextAscii() == "BROADCAST" && r2.TextAscii() == "BROADCAST", 3000),
                      $"r1='{r1.TextAscii()}' r2='{r2.TextAscii()}'");
                bool logged = WaitUntil(() => Logged(bs, LogDirection.Sent, "Broadcast to"), 3000);
                var sentMsgs = bs.Logs.Where(l => l.Direction == LogDirection.Sent).Select(l => l.Message).ToList();
                Check("브로드캐스트 대상 수가 로그에 표시됨",
                      logged && sentMsgs.Any(m => (m ?? "").Contains("2 client(s)")),
                      "[" + string.Join(" | ", sentMsgs) + "]");
            }
        }

        #endregion

        #region I. 인코딩

        private static void Encodings()
        {
            Check("새 연결의 기본 인코딩이 ASCII", new ConnectionModel().EncodingName == "ASCII");

            foreach (var (enc, text, expectLen) in new[]
            {
                ("ASCII", "ABC", 3),
                ("UTF-8", "재고", 6),
                ("EUC-KR", "재고", 4),
            })
            {
                var s = RunningServer(out int port, "ListenOnly", enc: enc);
                var c = Add(Client(port, enc));
                Start(c);
                WaitUntil(() => c.Status == "Connected", 3000);

                Select(c);
                _vm.SendText = text;
                _vm.SendCommand.Execute(null);

                bool ok = WaitUntil(() => s.Logs.Any(l => l.Direction == LogDirection.Received && l.Length == expectLen), 3000);
                var entry = s.Logs.LastOrDefault(l => l.Direction == LogDirection.Received);
                Check($"{enc}: '{text}'가 {expectLen}바이트로 전송", ok, "len=" + entry?.Length);
                Check($"{enc}: 서버가 올바르게 해석", (entry?.DecodedData ?? "") == text, entry?.DecodedData);

                Stop(c, s);
                Reset();
            }
        }

        #endregion

        #region J. 로그

        private static void Logging()
        {
            var s = RunningServer(out int port, "ListenOnly");
            var c = Add(Client(port));
            Start(c);
            WaitUntil(() => c.Status == "Connected", 3000);

            Select(c);
            foreach (var t in new[] { "ALPHA", "BRAVO", "ALPHA-2" })
            {
                _vm.SendText = t;
                _vm.SendCommand.Execute(null);
                Pump(200);
            }
            Select(s);
            Pump(300);

            var received = s.Logs.Where(l => l.Direction == LogDirection.Received).ToList();
            Check("수신 로그가 쌓임", received.Count == 3, "count=" + received.Count);

            var one = received.First();
            Check("로그에 시각이 기록됨", !string.IsNullOrEmpty(one.TimeText) && one.TimeText.Contains(":"), one.TimeText);
            Check("로그에 길이가 기록됨", one.ByteCountText.EndsWith(" B"), one.ByteCountText);
            Check("시스템 로그는 길이 대신 —", s.Logs.First(l => l.Direction == LogDirection.System).ByteCountText == "—");
            Check("Text 보기 본문", one.PayloadText == "ALPHA", one.PayloadText);
            Check("Hex 보기 본문", one.HexText == "41 4C 50 48 41", one.HexText);
            Check("Hex 문자열 끝에 공백이 없음", !one.HexText.EndsWith(" "));

            // 보기 모드
            _vm.IsTextView = false; _vm.IsSymbolView = true; Pump(80);
            Check("기호 보기로 전환", _vm.IsSymbolView && !_vm.IsTextView);
            _vm.IsSymbolView = false; _vm.IsHexView = true; Pump(80);
            Check("Hex 보기로 전환", _vm.IsHexView);
            _vm.IsHexView = false; _vm.IsTextView = true; Pump(80);

            // 자동 스크롤
            Check("자동 스크롤 기본 켜짐", _vm.IsAutoScrollEnabled);
            _vm.IsAutoScrollEnabled = false;
            Check("자동 스크롤 끄기", !_vm.IsAutoScrollEnabled);
            _vm.IsAutoScrollEnabled = true;

            // 검색
            _vm.SearchText = "ALPHA";
            Pump(400);
            Check("검색 시 '일치/전체' 표시", (_vm.LogCountText ?? "").Contains("/"), _vm.LogCountText);
            Note("검색 결과: " + _vm.LogCountText);
            _vm.SearchText = "";
            Pump(300);

            // Clear
            _vm.ClearLogCommand.Execute(null);
            Pump(150);
            Check("Clear Log가 로그와 카운터를 비움",
                  s.Logs.Count == 0 && s.BytesReceived == 0 && s.BytesSent == 0,
                  $"logs={s.Logs.Count} rx={s.BytesReceived}");

            // 순환 버퍼
            for (int i = 0; i < 1100; i++)
            {
                s.Logs.Add(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "n" + i });
            }
            Invoke(_vm, "UiUpdateTimer_Tick", null, null);
            Pump(200);
            Check("로그가 1000건을 넘어도 앱이 유지됨", s.Logs.Count >= 1000, "count=" + s.Logs.Count);

            // 처리량 지표
            Check("메시지 개수가 누적됨", c.MessageCount > 0, "count=" + c.MessageCount);
        }

        #endregion

        #region K. 파일 로깅

        private static void FileLogging()
        {
            string logPath = Path.Combine(Path.GetTempPath(), "fullqa-log-" + Guid.NewGuid().ToString("N") + ".log");

            int port = FreePort();
            var s = Server(port, "ListenOnly");
            s.IsRealtimeLogEnabled = true;
            s.LogFilePath = logPath;
            Add(s);
            Start(s);
            WaitUntil(() => s.Status == "Listening", 3000);

            using (var raw = new RawClient())
            {
                raw.Connect("127.0.0.1", port);
                raw.SendAscii("TO-FILE");
                WaitUntil(() => Logged(s, LogDirection.Received, "TO-FILE"), 3000);
            }
            Pump(300);
            Stop(s);
            Pump(200);

            Check("지정한 경로에 실시간 로그 파일이 생김", File.Exists(logPath), logPath);
            if (File.Exists(logPath))
            {
                string body = File.ReadAllText(logPath);
                Check("실시간 로그에 수신 내용이 기록됨", body.Contains("TO-FILE"));
                Check("실시간 로그에 서버 시작이 기록됨", body.Contains("Server started"));
                try { File.Delete(logPath); } catch (Exception) { }
            }

            // 로그 저장(Save Log)은 파일 대화상자를 쓰므로, 같은 형식으로 내보내지는지 확인합니다.
            var target = _vm.Connections.First();
            target.Logs.Add(new LogEntry { Timestamp = DateTime.Now, Direction = LogDirection.System, Message = "EXPORT-CHECK" });
            string exported = string.Join(Environment.NewLine, target.Logs.Select(l => l.DisplayMessage));
            Check("로그 내보내기 형식에 시각·방향·내용이 모두 들어감",
                  exported.Contains("EXPORT-CHECK") && exported.Contains("[System]"));

            // [보안] 악의적 세션이 시스템·시작프로그램 경로로 로그를 쓰려는 시나리오를 막는지 확인합니다.
            SecureLogging();
        }

        /// <summary>
        /// 관리자 권한 + 신뢰할 수 없는 세션 파일 조합에서, 보호 위치로의 로그 쓰기가 차단되는지 검증합니다.
        /// </summary>
        private static void SecureLogging()
        {
            // 모든 사용자 시작프로그램 폴더 안을 노린 악성 로그 경로입니다.
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            string evil = Path.Combine(startup, "fullqa-should-never-exist.log");

            Check("보호 경로 사전 검사(IsPathAllowed)가 시작프로그램을 거부", !LogService.IsPathAllowed(evil), evil);
            Check("보호 경로 사전 검사가 시스템 폴더를 거부",
                  !LogService.IsPathAllowed(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "x.log")));
            Check("보호 경로 사전 검사가 상대경로 우회(..\\)도 정규화 후 거부",
                  !LogService.IsPathAllowed(Path.Combine(startup, "..", Path.GetFileName(startup), "y.log")));
            Check("사용자 폴더는 정상 허용", LogService.IsPathAllowed(Path.Combine(Path.GetTempPath(), "ok.log")));

            int port = FreePort();
            var s = Server(port, "ListenOnly");
            s.IsRealtimeLogEnabled = true;
            s.LogFilePath = evil;   // 악성 세션이 지정한 경로
            Add(s);
            Start(s);
            WaitUntil(() => s.Status == "Listening", 3000);
            Pump(300);

            Check("보호 경로로의 실시간 로깅은 거부되어 파일이 생성되지 않음", !File.Exists(evil), evil);
            Check("거부되면 실시간 로깅 플래그가 꺼짐", !s.IsRealtimeLogEnabled);
            Check("거부 시 사용자에게 경고 배너 표시", _vm.Banners.Any(b => b.Kind == "log-path-rejected"),
                  string.Join(",", _vm.Banners.Select(b => b.Kind)));
            Check("서버 자체는 계속 정상 동작", s.Status == "Listening", s.Status);

            if (File.Exists(evil)) { try { File.Delete(evil); } catch (Exception) { } } // 혹시 만들어졌다면 정리
        }

        #endregion

        #region L. 자동 전달

        /// <summary>
        /// 클라이언트가 '상대가 보내오면 정해진 값으로 회신'하는 기능을 실제 소켓으로 확인합니다.
        /// 서버는 ListenOnly로 두어, 오가는 것이 전부 클라이언트의 자동 응답임을 보장합니다.
        /// </summary>
        private static void ClientAutoReply()
        {
            // 1) 고정 응답 - 서버가 보내면 클라이언트가 ACK로 회신
            {
                var srv = RunningServer(out int port, "ListenOnly");
                var cli = Client(port);
                cli.ResponsePattern = "ReplyAfterReceive";
                cli.ReplyMessage = "ACK";
                cli.IsReplyEndless = true;
                Add(cli);
                Start(cli);
                Check("클라이언트 접속", WaitUntil(() => cli.Status == "Connected", 3000), cli.Status);
                Check("카드 요약에 자동 응답 표시", (cli.MetaText ?? "").Contains("자동응답"), cli.MetaText);

                Select(srv);
                _vm.SendText = "HELLO";
                _vm.SendCommand.Execute(null);

                Check("서버가 보내면 클라이언트가 자동 회신",
                      WaitUntil(() => Logged(cli, LogDirection.Sent, "Auto reply"), 4000));
                Check("회신 내용이 서버에 도착", WaitUntil(() => Logged(srv, LogDirection.Received, "ACK"), 4000));
                Stop(srv, cli);
            }
            Reset();

            // 2) 지속 응답이 아니면 한 번만 회신
            {
                var srv = RunningServer(out int port, "ListenOnly");
                var cli = Client(port);
                cli.ResponsePattern = "ReplyAfterReceive";
                cli.ReplyMessage = "ONCE";
                cli.IsReplyEndless = false;
                Add(cli);
                Start(cli);
                WaitUntil(() => cli.Status == "Connected", 3000);

                Select(srv);
                for (int i = 0; i < 3; i++)
                {
                    _vm.SendText = "PROBE" + i;
                    _vm.SendCommand.Execute(null);
                    Pump(250);
                }
                Pump(500);
                int replies = cli.Logs.Count(l => l.Direction == LogDirection.Sent &&
                                                  (l.Message ?? "").Contains("Auto reply"));
                Check("지속 응답이 꺼져 있으면 1회만 회신", replies == 1, "회신=" + replies);
                Stop(srv, cli);
            }
            Reset();

            // 3) 규칙 기반 - 받은 내용에 따라 다른 값으로 회신
            {
                var srv = RunningServer(out int port, "ListenOnly");
                var cli = Client(port);
                cli.ResponsePattern = "ListenOnly"; // 고정 응답은 끔
                cli.Rules.Add(new ResponseRule { ReceiveData = "PING", SendData = "PONG" });
                cli.Rules.Add(new ResponseRule { ReceiveData = "STAT", SendData = "OK-200" });
                Add(cli);
                Start(cli);
                WaitUntil(() => cli.Status == "Connected", 3000);
                Check("카드 요약에 규칙 개수 표시", (cli.MetaText ?? "").Contains("규칙 2개"), cli.MetaText);

                Select(srv);
                _vm.SendText = "PING";
                _vm.SendCommand.Execute(null);
                Check("규칙에 맞는 회신이 서버에 도착", WaitUntil(() => Logged(srv, LogDirection.Received, "PONG"), 4000));

                _vm.SendText = "STAT";
                _vm.SendCommand.Execute(null);
                Check("다른 규칙은 다른 값으로 회신", WaitUntil(() => Logged(srv, LogDirection.Received, "OK-200"), 4000));

                _vm.SendText = "NOMATCH";
                _vm.SendCommand.Execute(null);
                Pump(700);
                int sent = cli.Logs.Count(l => l.Direction == LogDirection.Sent);
                Check("규칙에 없는 내용에는 회신하지 않음", sent == 2, "회신=" + sent);
                Stop(srv, cli);
            }
            Reset();

            // 4) 자동 응답을 켜지 않은 클라이언트는 예전 그대로 조용해야 합니다.
            {
                var srv = RunningServer(out int port, "ListenOnly");
                var cli = Add(Client(port)); // 기본값 그대로
                Start(cli);
                WaitUntil(() => cli.Status == "Connected", 3000);

                Select(srv);
                _vm.SendText = "QUIET";
                _vm.SendCommand.Execute(null);
                Check("서버가 보낸 내용을 클라이언트가 수신", WaitUntil(() => Logged(cli, LogDirection.Received, "QUIET"), 4000));
                Pump(700);
                Check("자동 응답을 안 켜면 아무것도 보내지 않음",
                      !cli.Logs.Any(l => l.Direction == LogDirection.Sent));
                Check("카드 요약에도 자동 응답이 안 적힘",
                      !(cli.MetaText ?? "").Contains("자동응답") && !(cli.MetaText ?? "").Contains("규칙"), cli.MetaText);
                Stop(srv, cli);
            }
            Reset();

            // 5) 조각 합치기 - ReceiveTimeout이 있으면 쪼개져 와도 규칙이 걸립니다.
            {
                int port = FreePort();
                using (var peer = new PeerServer(port))
                {
                    var cli = Client(port);
                    cli.ReceiveTimeout = 300;
                    cli.Rules.Add(new ResponseRule { ReceiveData = "PINGPONG", SendData = "MERGED" });
                    Add(cli);
                    Start(cli);
                    WaitUntil(() => cli.Status == "Connected", 3000);
                    Check("시험용 서버가 클라이언트를 받음", peer.WaitForClient(3000));

                    peer.SendAscii("PING"); // 한 전문을 일부러 두 조각으로 나눠 보냅니다
                    Pump(80);
                    peer.SendAscii("PONG");

                    Check("조각이 합쳐져 규칙이 걸림",
                          WaitUntil(() => peer.TextAscii().Contains("MERGED"), 5000), peer.TextAscii());
                    WaitUntil(() => cli.Logs.Any(l => l.Direction == LogDirection.Received), 3000);
                    Pump(400); // 혹시 두 건으로 나뉘어 들어오지는 않는지까지 확인
                    Check("합쳐진 한 건으로 로그에 남음",
                          cli.Logs.Count(l => l.Direction == LogDirection.Received) == 1,
                          "수신 로그=" + cli.Logs.Count(l => l.Direction == LogDirection.Received));
                    Stop(cli);
                }
            }
            Reset();

            // 6) ReceiveTimeout이 0이면 예전처럼 조각 그대로 들어옵니다. (기존 세션 호환)
            {
                int port = FreePort();
                using (var peer = new PeerServer(port))
                {
                    var cli = Add(Client(port)); // ReceiveTimeout 기본 0
                    Start(cli);
                    WaitUntil(() => cli.Status == "Connected", 3000);
                    peer.WaitForClient(3000);

                    peer.SendAscii("AAA");
                    WaitUntil(() => cli.Logs.Any(l => l.Direction == LogDirection.Received), 3000);
                    peer.SendAscii("BBB");
                    WaitUntil(() => cli.Logs.Count(l => l.Direction == LogDirection.Received) >= 2, 3000);
                    Pump(300);

                    Check("합치기를 끄면 조각마다 로그가 남음",
                          cli.Logs.Count(l => l.Direction == LogDirection.Received) == 2,
                          "수신 로그=" + cli.Logs.Count(l => l.Direction == LogDirection.Received));
                    Stop(cli);
                }
            }
            Reset();

            // 7) 주기 전송과 동시에 켜도 서로 방해하지 않아야 합니다.
            {
                var srv = RunningServer(out int port, "ListenOnly");
                var cli = Client(port);
                cli.ResponsePattern = "ReplyAfterReceive";
                cli.ReplyMessage = "ACK";
                cli.IsReplyEndless = true;
                Add(cli);
                Start(cli);
                WaitUntil(() => cli.Status == "Connected", 3000);

                Select(cli);
                _vm.IsPeriodicMode = true;
                _vm.PeriodicSendText = "HB";
                _vm.IntervalText = "100";
                _vm.PeriodicSendCommand.Execute(null);
                Check("주기 전송 시작", WaitUntil(() => cli.IsPeriodicSending, 2000));
                Check("하트비트가 서버에 도착", WaitUntil(() => Logged(srv, LogDirection.Received, "HB"), 4000));

                Select(srv);
                _vm.SendText = "POKE";
                _vm.SendCommand.Execute(null);
                Check("주기 전송 중에도 자동 응답이 나감",
                      WaitUntil(() => Logged(srv, LogDirection.Received, "ACK"), 4000));

                Select(cli);
                _vm.PeriodicSendCommand.Execute(null);
                Pump(300);
                Check("주기 전송 중지", !cli.IsPeriodicSending);
                Stop(srv, cli);
            }
            Reset();

            // 8) 대화상자에서 고른 값이 실제 연결까지 옮겨져야 합니다.
            //    예전에는 ViewModel이 if (isServer) 안에서만 값을 복사해서,
            //    클라이언트에서 자동 응답을 설정해도 조용히 버려졌습니다. (결함 #18)
            {
                var dlg = new AddConnectionWindow(false, null, _vm.Connections)
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
                dlg.Show(); dlg.UpdateLayout(); Pump(150);

                Check("클라이언트 대화상자에서 '수신 후 응답'을 고를 수 있음",
                      Field<RadioButton>(dlg, "ReplyAfterReceiveRadio")?.Visibility == Visibility.Visible &&
                      Field<RadioButton>(dlg, "ReplyAfterReceiveRadio")?.IsEnabled == true);

                Field<RadioButton>(dlg, "ReplyAfterReceiveRadio").IsChecked = true;
                Pump(120);
                Check("고르면 응답 데이터 입력란이 나타남",
                      Field<StackPanel>(dlg, "ReplyOptionsPanel")?.Visibility == Visibility.Visible);
                Check("고르면 수신 대기 입력란도 나타남",
                      Field<StackPanel>(dlg, "ReceiveTimeoutPanel")?.Visibility == Visibility.Visible);

                Field<TextBox>(dlg, "IpTextBox").Text = "127.0.0.1";
                Field<TextBox>(dlg, "PortTextBox").Text = FreePort().ToString();
                Field<TextBox>(dlg, "ReplyMessageTextBox").Text = "DLG-ACK";
                Field<CheckBox>(dlg, "EndlessReplyCheckBox").IsChecked = true;
                Field<TextBox>(dlg, "ReceiveTimeoutTextBox").Text = "250";
                Field<TextBox>(dlg, "ReceiveRuleTextBox").Text = "PING";
                Field<TextBox>(dlg, "SendRuleTextBox").Text = "PONG";
                Invoke(dlg, "AddRule_Click", null, null);
                Invoke(dlg, "OkButton_Click", null, null);
                Pump(120);

                Check("대화상자 결과에 응답 설정이 담김",
                      dlg.ResponsePattern == "ReplyAfterReceive" && dlg.ReplyMessage == "DLG-ACK" &&
                      dlg.IsReplyEndless && dlg.ReceiveTimeout == 250 && dlg.Rules.Count == 1,
                      $"{dlg.ResponsePattern}/{dlg.ReplyMessage}/{dlg.IsReplyEndless}/{dlg.ReceiveTimeout}/{dlg.Rules.Count}");

                // ViewModel이 대화상자 결과를 연결에 옮기는 부분만 따로 확인합니다.
                // (AddClient 커맨드는 모달을 띄워 자동화할 수 없으므로, 커맨드가 호출하는
                //  AddConnectionFromDialog 를 그대로 통과시킵니다)
                Invoke(_vm, "AddConnectionFromDialog", dlg);
                Pump(150);

                var made = _vm.Connections.LastOrDefault();
                Check("연결에 응답 패턴이 옮겨짐", made?.ResponsePattern == "ReplyAfterReceive", made?.ResponsePattern);
                Check("연결에 응답 데이터가 옮겨짐", made?.ReplyMessage == "DLG-ACK", made?.ReplyMessage);
                Check("연결에 지속 응답이 옮겨짐", made?.IsReplyEndless == true);
                Check("연결에 수신 대기가 옮겨짐", made?.ReceiveTimeout == 250, made?.ReceiveTimeout.ToString());
                Check("연결에 규칙이 옮겨짐", made?.Rules.Count == 1 && made.Rules[0].SendData == "PONG");
                Check("클라이언트로 만들어짐", made?.Type == "Client", made?.Type);

                dlg.Close();
            }
            Reset();

            // 9) 접속 인사 - 클라이언트가 먼저 말을 겁니다.
            {
                var srv = RunningServer(out int port, "ListenOnly");
                var cli = Client(port);
                cli.IsSendOnConnect = true;
                cli.SendOnConnectMessage = "LOGIN[STX]";
                Add(cli);
                Start(cli);
                WaitUntil(() => cli.Status == "Connected", 3000);

                Check("접속하자마자 보낸 내용이 서버에 도착",
                      WaitUntil(() => Logged(srv, LogDirection.Received, "LOGIN"), 4000));
                Check("제어문자 태그가 실제 바이트로 나감",
                      srv.Logs.Any(l => l.Direction == LogDirection.Received && l.Data != null &&
                                        l.Data.Length > 0 && l.Data[l.Data.Length - 1] == 0x02));
                Check("카드 요약에 접속 시 전송 표시", (cli.MetaText ?? "").Contains("접속 시 전송"), cli.MetaText);
                Stop(srv, cli);
            }
            Reset();

            // 10) 교착이 풀리는지 - 양쪽 다 '받으면 응답'인데 클라이언트가 첫 마디를 던집니다.
            //     접속 인사가 없으면 아무도 말하지 않아 영영 대기합니다.
            {
                var srv = RunningServer(out int port, "ReplyAfterReceive", "SRV-ACK", endless: true);
                var cli = Client(port);
                cli.ResponsePattern = "ReplyAfterReceive";
                cli.ReplyMessage = "CLI-ACK";
                cli.IsReplyEndless = false;        // 끝없이 왕복하지 않도록 1회만
                cli.IsSendOnConnect = true;
                cli.SendOnConnectMessage = "HELLO";
                Add(cli);
                Start(cli);
                WaitUntil(() => cli.Status == "Connected", 3000);

                Check("클라이언트의 첫 마디가 서버에 도착", WaitUntil(() => Logged(srv, LogDirection.Received, "HELLO"), 4000));
                Check("서버가 응답", WaitUntil(() => Logged(cli, LogDirection.Received, "SRV-ACK"), 4000));
                Check("클라이언트가 그 응답에 다시 회신", WaitUntil(() => Logged(srv, LogDirection.Received, "CLI-ACK"), 4000));
                Stop(srv, cli);
            }
            Reset();

            // 11) 접속 인사를 끄면 아무 일도 일어나지 않아야 합니다. (기본값 확인)
            {
                var srv = RunningServer(out int port, "ListenOnly");
                var cli = Add(Client(port));
                Start(cli);
                WaitUntil(() => cli.Status == "Connected", 3000);
                Pump(700);
                Check("접속 인사를 끄면 접속만 하고 조용함",
                      !srv.Logs.Any(l => l.Direction == LogDirection.Received));
                Stop(srv, cli);
            }
            Reset();

            // 12) 대화상자에서 접속 인사를 설정하면 연결까지 옮겨져야 합니다.
            {
                var dlg = new AddConnectionWindow(false, null, _vm.Connections)
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
                dlg.Show(); dlg.UpdateLayout(); Pump(150);

                Check("클라이언트 화면에 접속 인사 항목이 있음",
                      Field<Border>(dlg, "SendOnConnectCard")?.Visibility == Visibility.Visible);

                Field<CheckBox>(dlg, "SendOnConnectCheckBox").IsChecked = true;
                Pump(120);
                Check("켜면 보낼 데이터 입력란이 나타남",
                      Field<StackPanel>(dlg, "SendOnConnectDataPanel")?.Visibility == Visibility.Visible);

                Field<TextBox>(dlg, "IpTextBox").Text = "127.0.0.1";
                Field<TextBox>(dlg, "PortTextBox").Text = FreePort().ToString();

                // 데이터를 비운 채 확정하면 막아야 합니다.
                Invoke(dlg, "OkButton_Click", null, null);
                Check("보낼 데이터가 비어 있으면 확정을 막음",
                      (Field<TextBlock>(dlg, "StatusText")?.Text ?? "").Contains("먼저 보낼 데이터"),
                      Field<TextBlock>(dlg, "StatusText")?.Text);

                Field<TextBox>(dlg, "SendOnConnectTextBox").Text = "HELLO-DLG";
                Invoke(dlg, "OkButton_Click", null, null);
                Pump(120);
                Check("대화상자 결과에 접속 인사가 담김",
                      dlg.IsSendOnConnect && dlg.SendOnConnectMessage == "HELLO-DLG",
                      dlg.IsSendOnConnect + "/" + dlg.SendOnConnectMessage);

                Invoke(_vm, "AddConnectionFromDialog", dlg);
                Pump(150);
                var made = _vm.Connections.LastOrDefault();
                Check("연결에 접속 인사가 옮겨짐",
                      made?.IsSendOnConnect == true && made.SendOnConnectMessage == "HELLO-DLG",
                      made?.IsSendOnConnect + "/" + made?.SendOnConnectMessage);
                dlg.Close();
            }
            Reset();

            // 13) 서버 화면에는 접속 인사가 없어야 합니다. (서버는 '접속 시 1회 전송'이 따로 있음)
            {
                var dlg = new AddConnectionWindow(true, null, _vm.Connections)
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
                dlg.Show(); dlg.UpdateLayout(); Pump(150);
                Check("서버 화면에는 접속 인사 항목이 없음",
                      Field<Border>(dlg, "SendOnConnectCard")?.Visibility == Visibility.Collapsed);
                dlg.Close();
            }
        }

        private static void Forwarding()
        {
            // 1) 정상 중계
            using (var sink = new SinkServer())
            {
                int port = FreePort();
                var s = Server(port, "ListenOnly");
                s.IsForwardingEnabled = true;
                s.ForwardIpAddress = "127.0.0.1";
                s.ForwardPort = sink.Port;
                Add(s);
                Start(s);
                WaitUntil(() => s.Status == "Listening", 3000);

                Check("카드 요약에 전달 대상 표시",
                      (s.MetaText ?? "").Contains("전달 → 127.0.0.1:" + sink.Port), s.MetaText);

                using (var raw = new RawClient())
                {
                    raw.Connect("127.0.0.1", port);
                    raw.SendAscii("RELAY-1");
                    Check("수신 원본 바이트가 대상 서버로 그대로 전달",
                          WaitUntil(() => sink.TextAscii().Contains("RELAY-1"), 5000), sink.TextAscii());
                    Check("전달 성공이 로그에 남음", WaitUntil(() => Logged(s, LogDirection.Sent, "Forwarded to"), 3000));
                }
                Stop(s);
            }
            Reset();

            // 2) 대상이 꺼져 있을 때 보관했다가 재전송
            var sink2 = new SinkServer(startNow: false);
            try
            {
                int port = FreePort();
                var s = Server(port, "ListenOnly");
                s.IsForwardingEnabled = true;
                s.ForwardIpAddress = "127.0.0.1";
                s.ForwardPort = sink2.Port;
                Add(s);
                Start(s);
                WaitUntil(() => s.Status == "Listening", 3000);

                using (var raw = new RawClient())
                {
                    raw.Connect("127.0.0.1", port);
                    raw.SendAscii("QUEUED-A");
                    Pump(400);
                    raw.SendAscii("QUEUED-B");
                    Pump(800);

                    Check("대상이 꺼져 있으면 아직 전달되지 않음", sink2.TextAscii().Length == 0, sink2.TextAscii());

                    sink2.Start();
                    Check("대상이 살아나면 보관분이 순서대로 재전송",
                          WaitUntil(() => sink2.TextAscii().Contains("QUEUED-A") && sink2.TextAscii().Contains("QUEUED-B"), 15000),
                          sink2.TextAscii());
                    Check("재전송 순서가 유지됨",
                          sink2.TextAscii().IndexOf("QUEUED-A", StringComparison.Ordinal) <
                          sink2.TextAscii().IndexOf("QUEUED-B", StringComparison.Ordinal));
                }
                Stop(s);
            }
            finally { sink2.Dispose(); }
        }

        #endregion

        #region M. 실패 처리

        private static void Failures()
        {
            // 포트 점유 실패
            int port = FreePort();
            var squatter = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            squatter.Start();

            var s = Add(Server(port, "ListenOnly"));
            Start(s);

            Check("바인딩 실패 시 상태가 Error", WaitUntil(() => s.Status == "Error", 4000), s.Status);
            Check("모달 대신 인라인 배너", WaitUntil(() => _vm.Banners.Any(b => b.Kind == "bind-failed"), 4000));

            var banner = _vm.Banners.FirstOrDefault(b => b.Kind == "bind-failed");
            Check("배너에 포트 번호", (banner?.Title ?? "").Contains(port.ToString()), banner?.Title);
            Check("배너에 원인 코드", (banner?.TechnicalDetail ?? "").Contains("AddressAlreadyInUse"), banner?.TechnicalDetail);
            Check("재시도/포트 변경/점유 확인 동작 제공",
                  banner?.PrimaryActionCommand != null && banner?.SecondaryActionCommand != null &&
                  banner?.TertiaryActionCommand != null);
            Check("연결 카드에 오류 문구", !string.IsNullOrEmpty(s.ErrorText), s.ErrorText);

            banner.TertiaryActionCommand.Execute(null);
            Check("점유 프로세스 조회 결과가 채워짐",
                  WaitUntil(() => !string.IsNullOrEmpty(banner.StatusNote) && banner.StatusNote != "조회 중...", 15000),
                  banner.StatusNote);
            Note("조회 결과: " + banner.StatusNote);

            banner.DismissCommand.Execute(banner);
            Check("배너 닫기", !_vm.Banners.Contains(banner));

            squatter.Stop();
            Pump(300);
            Start(s);
            Check("점유가 풀린 뒤 재시도하면 시작됨", WaitUntil(() => s.Status == "Listening", 4000), s.Status);
            Check("성공 시 오류 문구가 지워짐", string.IsNullOrEmpty(s.ErrorText), s.ErrorText);
            Stop(s);
            Reset();

            // 접속 실패
            int dead = FreePort();
            var c = Add(Client(dead));
            Start(c);
            Check("접속 실패 시 배너", WaitUntil(() => _vm.Banners.Any(b => b.Kind == "connect-failed"), 5000),
                  string.Join(",", _vm.Banners.Select(b => b.Kind)));
            Reset();

            // 상대 종료
            var s2 = RunningServer(out int p2, "ListenOnly");
            var c2 = Add(Client(p2));
            Start(c2);
            WaitUntil(() => c2.Status == "Connected", 3000);
            Stop(s2);
            Check("상대가 끊으면 재연결 배너", WaitUntil(() => _vm.Banners.Any(b => b.Kind == "peer-closed"), 4000));
            Check("클라이언트 상태가 정리됨", WaitUntil(() => c2.Status == "Stopped", 3000), c2.Status);

            // 배너 상한
            for (int i = 0; i < 6; i++)
            {
                Invoke(_vm, "ShowBanner", new BannerItem { Kind = "k" + i, Title = "t" + i });
            }
            Pump(120);
            Check("배너는 최대 3건만 유지", _vm.Banners.Count <= 3, "count=" + _vm.Banners.Count);
        }

        #endregion

        #region N. 세션

        private static void Session()
        {
            // 저장할 세션이 없을 때 'Save Session'을 막는지 확인합니다.
            // 예전에는 연결이 하나도 없어도 "[]" 뿐인 빈 파일이 저장됐습니다.
            Check("연결이 없으면 Save Session이 비활성", !_vm.SaveSessionCommand.CanExecute(null));
            _vm.Banners.Clear();
            _vm.SaveSessionCommand.Execute(null); // 가드가 파일 대화상자 전에 막아야 합니다
            Pump(150);
            Check("연결이 없을 때 실행하면 파일 대화상자 대신 경고 배너",
                  _vm.Banners.Any(b => b.Kind == "session-save-empty"));
            _vm.Banners.Clear();

            var probe = Add(Server(FreePort(), "ListenOnly")); // 시작하지 않으므로 포트를 물지 않습니다
            Check("연결이 하나라도 있으면 Save Session이 활성", _vm.SaveSessionCommand.CanExecute(null));
            _vm.Connections.Remove(probe);
            Pump(120);
            Check("연결을 모두 지우면 Save Session이 다시 비활성", !_vm.SaveSessionCommand.CanExecute(null));

            int port = FreePort();
            var s = Server(port, "ReplyAfterReceive", "ACK", endless: true, enc: "EUC-KR");
            s.Rules.Add(new ResponseRule { ReceiveData = "PING", SendData = "PONG" });
            s.IsForwardingEnabled = true;
            s.ForwardIpAddress = "127.0.0.1";
            s.ForwardPort = 9999;
            s.AutoStart = false;
            Add(s);

            string path = Path.Combine(Path.GetTempPath(), "fullqa-session.json");
            if (File.Exists(path)) File.Delete(path);
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(_vm.Connections,
                Newtonsoft.Json.Formatting.Indented));

            Check("세션 파일 생성", File.Exists(path));
            string json = File.ReadAllText(path);
            Check("설정이 JSON에 담김",
                  json.Contains("\"ResponsePattern\": \"ReplyAfterReceive\"") &&
                  json.Contains("\"EncodingName\": \"EUC-KR\"") &&
                  json.Contains("\"IsForwardingEnabled\": true") &&
                  json.Contains("\"PING\""));
            Check("런타임 값은 저장되지 않음",
                  !json.Contains("\"Manager\"") && !json.Contains("\"Logs\"") &&
                  !json.Contains("\"MessageCount\"") && !json.Contains("\"MetaText\""));

            _vm.Connections.Clear();
            Pump(100);

            RecentSessionService.Add(path);
            _vm.OpenRecentSessionCommand.Execute(path);
            Pump(500);

            Check("불러오기로 연결 복원", _vm.Connections.Count == 1, "count=" + _vm.Connections.Count);
            var l = _vm.Connections.FirstOrDefault();
            Check("응답 패턴 복원", l?.ResponsePattern == "ReplyAfterReceive", l?.ResponsePattern);
            Check("인코딩 복원", l?.EncodingName == "EUC-KR", l?.EncodingName);
            Check("규칙 복원", l?.Rules.Count == 1 && l.Rules[0].SendData == "PONG");
            Check("자동 전달 설정 복원", l != null && l.IsForwardingEnabled && l.ForwardPort == 9999);
            Check("AutoStart=false면 자동 시작하지 않음", l?.Status == "Stopped", l?.Status);
            Check("카드 요약이 다시 계산됨", !string.IsNullOrEmpty(l?.MetaText), l?.MetaText);
            Check("최근 세션 목록에 반영", _vm.RecentSessionPaths.Any(p => p == path));

            // AutoStart=true 로 다시 불러오기
            _vm.Connections.Clear();
            Pump(100);
            var auto = Server(FreePort(), "ListenOnly");
            auto.AutoStart = true;
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(
                new[] { auto }, Newtonsoft.Json.Formatting.Indented));
            _vm.OpenRecentSessionCommand.Execute(path);
            Check("AutoStart=true면 불러오면서 자동 시작",
                  WaitUntil(() => _vm.Connections.FirstOrDefault()?.Status == "Listening", 4000),
                  _vm.Connections.FirstOrDefault()?.Status);

            // 새 필드가 없는 예전 형식 세션도 그대로 읽혀야 합니다.
            _vm.Connections.Clear();
            Pump(100);
            File.WriteAllText(path,
                "[{\"Type\":\"Client\",\"IpAddress\":\"127.0.0.1\",\"Port\":19999," +
                "\"Address\":\"127.0.0.1:19999\",\"Status\":\"Stopped\"," +
                "\"ResponsePattern\":\"Echo\",\"EncodingName\":\"ASCII\"," +
                "\"ReceiveTimeout\":0,\"AutoStart\":false}]");
            _vm.OpenRecentSessionCommand.Execute(path);
            Pump(400);
            var legacy = _vm.Connections.FirstOrDefault();
            Check("새 필드가 없는 예전 세션도 읽힘", legacy != null && legacy.Port == 19999);
            Check("예전 세션은 접속 인사가 꺼진 상태", legacy?.IsSendOnConnect == false);
            Check("예전 세션은 조각 합치기가 꺼진 상태", legacy?.ReceiveTimeout == 0, legacy?.ReceiveTimeout.ToString());

            try { File.Delete(path); } catch (Exception) { }
        }

        #endregion

        #region O. 테마

        private static void Theme()
        {
            var s = RunningServer(out int port, "Echo");
            var c = Add(Client(port));
            Start(c);
            WaitUntil(() => c.Status == "Connected", 3000);
            Select(c);
            _vm.SendText = "[STX]ACK|00419[ETX]";
            _vm.SendCommand.Execute(null);
            Pump(400);
            Select(s);
            Pump(200);

            foreach (var (mode, expectBg) in new[] { ("Light", "#FFF3F3F3"), ("Dark", "#FF202020") })
            {
                _vm.SetThemeCommand.Execute(mode);
                Pump(400);

                var bg = (SolidColorBrush)Application.Current.TryFindResource("WindowBackgroundBrush");
                Check($"{mode}: 팔레트가 교체됨", bg?.Color.ToString() == expectBg, bg?.Color.ToString());
                Check($"{mode}: 창이 즉시 다시 그려짐 (DynamicResource)",
                      (_w.Background as SolidColorBrush)?.Color.ToString() == expectBg,
                      (_w.Background as SolidColorBrush)?.Color.ToString());
                Check($"{mode}: 메뉴 체크 상태 반영",
                      (mode == "Dark" ? _vm.IsDarkTheme : _vm.IsLightTheme));

                // 메뉴 팝업 가독성
                var menu = Find<Menu>(_w);
                foreach (MenuItem top in menu.Items.OfType<MenuItem>())
                {
                    top.IsSubmenuOpen = true;
                    Pump(350);
                    var popup = top.Template.FindName("PART_Popup", top) as Popup;
                    var surface = popup?.Child == null ? null : SolidBackground(Find<ContentPresenter>(popup.Child) ?? popup.Child);
                    var item = top.Items.OfType<MenuItem>().FirstOrDefault();
                    var fg = (item?.Foreground as SolidColorBrush)?.Color;

                    if (surface.HasValue && fg.HasValue)
                    {
                        double ratio = Contrast(fg.Value, surface.Value);
                        Check($"{mode}: '{(top.Header ?? "").ToString().Split(' ')[0]}' 메뉴 글자가 읽힘 ({ratio:0.0}:1)",
                              ratio >= 4.5, $"fg={fg} bg={surface}");
                    }
                    else Check($"{mode}: 메뉴 팝업 색을 읽음", false, "surface/fg 없음");

                    top.IsSubmenuOpen = false;
                    Pump(120);
                }

                CaptureScreen("theme-" + mode.ToLowerInvariant());
            }

            // 컨텍스트 메뉴
            var lv = Find<ListView>(_w, x => x.Name == "ConnectionListView");
            if (lv?.ContextMenu != null)
            {
                lv.ContextMenu.PlacementTarget = lv;
                lv.ContextMenu.Placement = PlacementMode.Center;
                lv.ContextMenu.IsOpen = true;
                Pump(400);

                var border = Find<Border>(lv.ContextMenu, b => b.Background is SolidColorBrush sb && sb.Color.A > 200);
                var bg = (border?.Background as SolidColorBrush)?.Color;
                var fg = (lv.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault()?.Foreground as SolidColorBrush)?.Color;
                if (bg.HasValue && fg.HasValue)
                {
                    Check($"다크: 컨텍스트 메뉴 글자가 읽힘 ({Contrast(fg.Value, bg.Value):0.0}:1)",
                          Contrast(fg.Value, bg.Value) >= 4.5, $"fg={fg} bg={bg}");
                }
                lv.ContextMenu.IsOpen = false;
                Pump(150);
            }

            // 저장/복원
            _vm.SetThemeCommand.Execute("Dark");
            Pump(200);
            string themeFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme.json");
            Check("선택한 테마가 파일에 저장됨",
                  File.Exists(themeFile) && File.ReadAllText(themeFile).Contains("Dark"), themeFile);

            _vm.SetThemeCommand.Execute("System");
            Pump(200);
            Check("시스템 설정 따르기로 되돌아감", _vm.IsSystemTheme);
        }

        #endregion

        #region P. 자원 정리

        private static void Cleanup()
        {
            // 중지 시 포트 반납
            var s = RunningServer(out int port, "ListenOnly");
            Check("실행 중에는 포트 점유", !PortIsFree(port));
            Stop(s);
            Check("중지하면 포트 반납", WaitUntil(() => PortIsFree(port), 3000));
            Reset();

            // 중지 경합 반복
            int badStatus = 0, falseBanner = 0, noisy = 0;
            const int rounds = 6;
            for (int i = 0; i < rounds; i++)
            {
                var srv = RunningServer(out int p, "Echo");
                var cli = Add(Client(p));
                Start(cli);
                WaitUntil(() => cli.Status == "Connected", 3000);

                Stop(srv, cli);
                Pump(400);

                if (srv.Status != "Stopped") badStatus++;
                if (_vm.Banners.Any(b => b.Kind == "bind-failed")) falseBanner++;
                if (srv.Logs.Any(l => (l.Message ?? "").StartsWith("Error:") ||
                                      (l.Message ?? "").StartsWith("Socket Error:"))) noisy++;
                Reset();
            }
            Check($"{rounds}회 중지 반복: 상태가 Error로 남지 않음", badStatus == 0, "발생=" + badStatus);
            Check($"{rounds}회 중지 반복: 가짜 포트 배너 없음", falseBanner == 0, "발생=" + falseBanner);
            Check($"{rounds}회 중지 반복: 오류 로그 노이즈 없음", noisy == 0, "발생=" + noisy);

            // 연속 시작 방지
            var s2 = Add(Server(FreePort(), "ListenOnly"));
            Select(s2);
            _vm.StartConnectionCommand.Execute(null);
            Check("시작 직후 상태가 Starting으로 표시됨", s2.Status == "Starting" || s2.Status == "Listening", s2.Status);
            _vm.StartConnectionCommand.Execute(null); // 곧바로 한 번 더
            WaitUntil(() => s2.Status == "Listening", 4000);
            Pump(400);
            Check("연속 시작해도 가짜 포트 충돌이 나지 않음", s2.Status == "Listening", s2.Status);
            Check("연속 시작해도 배너가 뜨지 않음", !_vm.Banners.Any(b => b.Kind == "bind-failed"));
            Stop(s2);
            Check("정리 후 포트 반납", WaitUntil(() => PortIsFree(s2.Port), 3000));
        }

        #endregion
    }
}
