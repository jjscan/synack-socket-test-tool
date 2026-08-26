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
            Check("버전이 내림차순으로 정렬됨",
                  ReleaseHistory.All.Select(r => r.Version).SequenceEqual(
                      new[] { "v2.0.1", "v2.0.0", "v1.0.1", "v1.0.0" }),
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
            Check("좌측 버전 목록에 4개 항목", list != null && list.Items.Count == 4, list?.Items.Count.ToString());
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
            Check("서버 모드에서 서버 전용 설정이 보임",
                  Field<StackPanel>(dlg, "ServerOptionsPanel")?.Visibility == Visibility.Visible);
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
            Check("클라이언트 모드에서는 서버 전용 설정이 숨겨짐",
                  Field<StackPanel>(dlg2, "ServerOptionsPanel")?.Visibility == Visibility.Collapsed);

            // 모드 전환
            Field<RadioButton>(dlg2, "ServerModeRadio").IsChecked = true;
            Pump(120);
            Check("대화상자 안에서 서버로 전환 가능",
                  dlg2.IsServerMode && Field<StackPanel>(dlg2, "ServerOptionsPanel")?.Visibility == Visibility.Visible);
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
        }

        #endregion

        #region L. 자동 전달

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
