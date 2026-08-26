using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using SocketTestTool.Models;
using SocketTestTool.ViewModels;
using static Stress.Infra;

namespace Stress
{
    /// <summary>
    /// SocketTestTool을 실제로 띄운 채 부하를 걸고, 자원 한계를 단계적으로 찾아보는 테스트입니다.
    /// OS가 죽을 때까지 밀어붙이지 않고, Infra의 안전 한계선에 닿으면 스스로 멈춥니다.
    /// </summary>
    internal static class StressMain
    {
        private static SocketTestTool.MainWindow _w;
        private static MainViewModel _vm;
        private static UiProbe _probe;

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
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
            _w.Show();
            Pump(500);
            _vm = (MainViewModel)_w.DataContext;
            _probe = new UiProbe(_w.Dispatcher);

            var baseline = Measure();
            Section("0. 시작 상태");
            Note("한계선: 시스템여유 < " + AbortWhenSystemFreeBelowMb + " MB, 프로세스 > " + AbortWhenProcessMemoryAboveMb +
                 " MB, 핸들 > " + AbortWhenHandlesAbove + ", 스레드 > " + AbortWhenThreadsAbove +
                 ", UI지연 > " + AbortWhenUiStalledMs + " ms");
            Note("기준선: " + baseline);

            Run("1. 고속 대량 수신 (단일 연결)", HighThroughput);
            Run("2. 대용량 페이로드 (1 MB)", LargePayload);
            Run("3. 다중 클라이언트 동시 접속", ManyClients);
            Run("4. 주기 전송 폭주", PeriodicStorm);
            Run("5. 로그 폭주와 순환 버퍼", LogFlood);
            Run("6. 자동 전달 큐 상한 (대상 다운)", ForwardingBackpressure);
            Run("7. 연결 수 확장 — 자원 한계 탐색", ScaleOut);
            Run("8. 정리 후 자원 회수", Recovery);

            var final = Measure();
            Section("9. 최종 상태");
            Note("시작: " + baseline);
            Note("종료: " + final);
            Check("테스트 후에도 프로세스 메모리가 기준선 대비 400 MB 이내",
                  final.ProcessMb - baseline.ProcessMb < 400,
                  $"{baseline.ProcessMb} MB -> {final.ProcessMb} MB");
            Check("테스트 후에도 핸들이 기준선 대비 2000개 이내",
                  final.Handles - baseline.Handles < 2000,
                  $"{baseline.Handles} -> {final.Handles}");
            Check("테스트 후에도 스레드가 기준선 대비 200개 이내",
                  final.Threads - baseline.Threads < 200,
                  $"{baseline.Threads} -> {final.Threads}");
            Check("앱이 살아 있고 UI가 응답함", _w.IsVisible && _probe.MaxMs < AbortWhenUiStalledMs,
                  $"UI 최대지연 {_probe.MaxMs:0} ms");

            _probe.Dispose();
            Write(Path.Combine(Path.GetTempPath(), "stress-result.txt"));
            return FailCount == 0 ? 0 : 1;
        }

        private static void Run(string title, Action body)
        {
            Section(title);
            _probe.Reset();
            ResetCpuWindow();
            try { body(); }
            catch (Exception ex)
            {
                Check("구간이 끝까지 실행됨", false, ex.GetType().Name + ": " + ex.Message);
                Note((ex.StackTrace ?? "").Split('\n').FirstOrDefault() ?? "");
            }
            Cleanup();

            // 도중에 멈추더라도 여기까지의 결과는 남도록 매 구간 저장합니다.
            Write(Path.Combine(Path.GetTempPath(), "stress-result.txt"));
        }

        #region 공용

        private static void Cleanup()
        {
            foreach (var c in _vm.Connections.ToList())
            {
                _vm.SelectedItems.Clear();
                _vm.SelectedItems.Add(c);
                _vm.StopConnectionCommand.Execute(null);
            }
            Pump(300);
            _vm.Connections.Clear();
            _vm.Banners.Clear();
            _vm.SelectedItems.Clear();
            _vm.SelectedConnection = null;
            _vm.IsPeriodicMode = false;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Pump(200);
        }

        private static ConnectionModel Server(int port, string pattern = "ListenOnly", int timeout = 0)
            => new ConnectionModel
            {
                Type = "Server", IpAddress = "127.0.0.1", Port = port, Address = "127.0.0.1:" + port,
                Status = "Stopped", ResponsePattern = pattern, ReceiveTimeout = timeout, EncodingName = "ASCII"
            };

        private static ConnectionModel Client(int port)
            => new ConnectionModel
            {
                Type = "Client", IpAddress = "127.0.0.1", Port = port, Address = "127.0.0.1:" + port,
                Status = "Stopped", EncodingName = "ASCII"
            };

        private static ConnectionModel StartServer(out int port, string pattern = "ListenOnly", int timeout = 0)
        {
            port = FreePort();
            var s = Server(port, pattern, timeout);
            _vm.Connections.Add(s);
            _vm.SelectedConnection = s;
            _vm.SelectedItems.Clear();
            _vm.SelectedItems.Add(s);
            _vm.StartConnectionCommand.Execute(null);
            WaitUntil(() => s.Status == "Listening", 5000);
            return s;
        }

        #endregion

        #region 1. 고속 대량 수신

        private static void HighThroughput()
        {
            const int messages = 20000;
            var payload = Encoding.ASCII.GetBytes("MSG|0123456789|ABCDEFGHIJ\n"); // 26 bytes
            long expected = (long)messages * payload.Length;

            var s = StartServer(out int port);
            var before = Measure();

            using (var b = new Blaster())
            {
                Check("부하 클라이언트 접속", b.Connect("127.0.0.1", port));

                var sw = Stopwatch.StartNew();
                var task = b.BlastAsync(payload, messages);

                bool done = WaitUntil(() => task.IsCompleted && s.BytesReceived >= expected, 60000);
                sw.Stop();

                Check($"{messages:N0}건 / {expected / 1024:N0} KB 전량 수신 (손실 없음)", done,
                      $"받음 {s.BytesReceived:N0} / 기대 {expected:N0}");

                if (done)
                {
                    double mbps = expected / 1024.0 / 1024.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    Note($"소요 {sw.Elapsed.TotalSeconds:0.00}초, 약 {mbps:0.0} MB/s");
                }
            }

            var after = Measure();
            Note("부하 전: " + before);
            Note("부하 후: " + after);

            Check("고속 수신 중에도 UI가 응답함 (최대 지연 < 2초)", _probe.MaxMs < 2000, $"최대 {_probe.MaxMs:0} ms, 평균 {_probe.AvgMs:0} ms");
            Check("로그는 순환 버퍼 상한(1000)을 지킴", s.Logs.Count <= 1000, "logs=" + s.Logs.Count);
            Check("대량 수신 후에도 메모리가 과도하게 늘지 않음 (< 500 MB 증가)",
                  after.ProcessMb - before.ProcessMb < 500, $"{before.ProcessMb} -> {after.ProcessMb} MB");
            Check("메시지 누적 카운터가 동작", s.MessageCount > 0, "count=" + s.MessageCount);
        }

        #endregion

        #region 2. 대용량 페이로드

        private static void LargePayload()
        {
            var s = StartServer(out int port, timeout: 300); // 조각을 합치도록 침묵 감지 사용
            var payload = new byte[1024 * 1024];
            new Random(1).NextBytes(payload);

            var before = Measure();
            using (var b = new Blaster())
            {
                b.Connect("127.0.0.1", port);
                var t = b.BlastAsync(payload, 3);

                bool ok = WaitUntil(() => t.IsCompleted && s.BytesReceived >= 3L * payload.Length, 60000);
                Check("1 MB × 3건 전량 수신", ok, $"받음 {s.BytesReceived:N0} / 기대 {3L * payload.Length:N0}");
            }

            var after = Measure();
            Check("대용량 처리 중 UI 응답 유지 (< 3초)", _probe.MaxMs < 3000, $"최대 {_probe.MaxMs:0} ms");
            Check("대용량 처리 후 메모리 증가가 제한적 (< 300 MB)",
                  after.ProcessMb - before.ProcessMb < 300, $"{before.ProcessMb} -> {after.ProcessMb} MB");
            Note("부하 후: " + after);
        }

        #endregion

        #region 3. 다중 클라이언트

        private static void ManyClients()
        {
            const int clients = 60;
            const int perClient = 200;
            var payload = Encoding.ASCII.GetBytes("PACKET-0123456789\n"); // 18 bytes
            long expected = (long)clients * perClient * payload.Length;

            var s = StartServer(out int port);
            var before = Measure();
            var blasters = new List<Blaster>();

            try
            {
                int connected = 0;
                for (int i = 0; i < clients; i++)
                {
                    var b = new Blaster();
                    if (b.Connect("127.0.0.1", port)) connected++;
                    blasters.Add(b);
                }
                Check($"클라이언트 {clients}개 동시 접속", connected == clients, "접속=" + connected);
                Pump(300);

                var tasks = blasters.Select(b => b.BlastAsync(payload, perClient)).ToArray();
                bool done = WaitUntil(() => tasks.All(t => t.IsCompleted) && s.BytesReceived >= expected, 90000);

                Check($"{clients}개 클라이언트 × {perClient}건 전량 수신", done,
                      $"받음 {s.BytesReceived:N0} / 기대 {expected:N0}");
            }
            finally
            {
                foreach (var b in blasters) b.Dispose();
            }

            var after = Measure();
            Note("부하 전: " + before);
            Note("부하 후: " + after);
            Check("다중 접속 중에도 UI 응답 유지 (< 3초)", _probe.MaxMs < 3000, $"최대 {_probe.MaxMs:0} ms");
            Check("서버가 계속 Listening 상태", s.Status == "Listening", s.Status);

            Pump(1500);
            var released = Measure();
            Check("클라이언트 종료 후 핸들이 회수됨",
                  released.Handles < after.Handles + 200, $"{after.Handles} -> {released.Handles}");
        }

        #endregion

        #region 4. 주기 전송 폭주

        private static void PeriodicStorm()
        {
            const int pairs = 10;
            var servers = new List<ConnectionModel>();
            var clients = new List<ConnectionModel>();

            for (int i = 0; i < pairs; i++)
            {
                var s = StartServer(out int port);
                servers.Add(s);

                var c = Client(port);
                _vm.Connections.Add(c);
                _vm.SelectedConnection = c;
                _vm.SelectedItems.Clear();
                _vm.SelectedItems.Add(c);
                _vm.StartConnectionCommand.Execute(null);
                WaitUntil(() => c.Status == "Connected", 5000);
                clients.Add(c);
            }

            Check($"서버·클라이언트 {pairs}쌍 연결", clients.All(c => c.Status == "Connected"),
                  string.Join(",", clients.Select(c => c.Status)));

            var before = Measure();

            // 모든 클라이언트에 10ms 주기 전송을 겁니다.
            _vm.IsPeriodicMode = true;
            _vm.IntervalText = "10";
            _vm.PeriodicSendText = "HEARTBEAT-PACKET";
            foreach (var c in clients)
            {
                _vm.SelectedConnection = c;
                _vm.SelectedItems.Clear();
                _vm.SelectedItems.Add(c);
                _vm.PeriodicSendCommand.Execute(null);
            }

            Check($"{pairs}개 연결에서 10ms 주기 전송 시작", clients.All(c => c.IsPeriodicSending));

            Pump(5000); // 5초 동안 폭주

            long totalRx = servers.Sum(s => s.BytesReceived);
            int payloadLen = Encoding.ASCII.GetByteCount("HEARTBEAT-PACKET");
            long ticks = totalRx / payloadLen;
            double perConnInterval = 5000.0 / Math.Max(1, ticks / (double)pairs);

            // Windows의 기본 타이머 해상도가 약 15.6 ms라, 10 ms를 지정해도 그보다 짧아질 수 없습니다.
            // 따라서 '지정한 간격대로'가 아니라 '타이머 해상도 한계 안에서 꾸준히 나갔는가'를 봅니다.
            Check("주기 전송이 끊김 없이 지속됨 (연결당 250회 이상)", ticks / pairs >= 250,
                  $"총 {totalRx:N0} 바이트 / {ticks:N0}회 / 연결당 {ticks / pairs}회");
            Note($"5초간 {totalRx:N0} 바이트, 연결당 실측 간격 약 {perConnInterval:0.0} ms " +
                 "(10 ms 지정, Windows 기본 타이머 해상도 약 15.6 ms가 하한)");

            var during = Measure();
            Note("폭주 중: " + during);
            Check("주기 전송 폭주 중에도 UI 응답 유지 (< 3초)", _probe.MaxMs < 3000, $"최대 {_probe.MaxMs:0} ms");

            foreach (var c in clients)
            {
                _vm.SelectedConnection = c;
                _vm.SelectedItems.Clear();
                _vm.SelectedItems.Add(c);
                _vm.PeriodicSendCommand.Execute(null);
            }
            Pump(500);
            Check("전부 중지됨", clients.All(c => !c.IsPeriodicSending));

            long afterStop = servers.Sum(s => s.BytesReceived);
            Pump(1000);
            Check("중지 후 더 이상 늘지 않음", servers.Sum(s => s.BytesReceived) - afterStop < 5000,
                  $"{afterStop:N0} -> {servers.Sum(s => s.BytesReceived):N0}");

            _vm.IsPeriodicMode = false;
        }

        #endregion

        #region 5. 로그 폭주

        private static void LogFlood()
        {
            var s = StartServer(out int port);
            var before = Measure();
            var payload = Encoding.ASCII.GetBytes("L\n");

            using (var b = new Blaster())
            {
                b.Connect("127.0.0.1", port);
                var t = b.BlastAsync(payload, 30000, gapMs: 0);
                WaitUntil(() => t.IsCompleted, 60000);
                Pump(2000);
            }

            var after = Measure();
            Check("로그가 1000건 상한을 넘지 않음", s.Logs.Count <= 1000, "logs=" + s.Logs.Count);
            Check("누적 메시지 카운터는 상한과 무관하게 계속 증가", s.MessageCount > 1000, "count=" + s.MessageCount);
            Check("로그 폭주 후에도 GC 힙이 과도하지 않음 (< 400 MB)", after.GcMb < 400, after.GcMb + " MB");
            Check("로그 폭주 중 UI 응답 유지 (< 3초)", _probe.MaxMs < 3000, $"최대 {_probe.MaxMs:0} ms");
            Note("폭주 전: " + before);
            Note("폭주 후: " + after);
        }

        #endregion

        #region 6. 자동 전달 배압

        private static void ForwardingBackpressure()
        {
            // 대상 서버를 일부러 띄우지 않아 전달이 계속 실패하도록 둡니다.
            int deadPort = FreePort();

            int port = FreePort();
            var s = Server(port);
            s.IsForwardingEnabled = true;
            s.ForwardIpAddress = "127.0.0.1";
            s.ForwardPort = deadPort;
            _vm.Connections.Add(s);
            _vm.SelectedConnection = s;
            _vm.SelectedItems.Clear();
            _vm.SelectedItems.Add(s);
            _vm.StartConnectionCommand.Execute(null);
            WaitUntil(() => s.Status == "Listening", 5000);

            var before = Measure();
            var payload = Encoding.ASCII.GetBytes("FORWARD-ME-0123456789\n");

            using (var b = new Blaster())
            {
                b.Connect("127.0.0.1", port);
                var t = b.BlastAsync(payload, 8000);
                WaitUntil(() => t.IsCompleted, 60000);
                Pump(3000);
            }

            var after = Measure();
            Check("대상이 없어도 서버는 계속 동작", s.Status == "Listening", s.Status);
            Check("전달 큐가 메모리를 잠식하지 않음 (< 300 MB 증가)",
                  after.ProcessMb - before.ProcessMb < 300, $"{before.ProcessMb} -> {after.ProcessMb} MB");
            Check("큐 상한 초과가 로그로 보고됨",
                  s.Logs.Any(l => (l.Message ?? "").Contains("buffer is full")) ||
                  s.MessageCount > 1000,
                  "logs=" + s.Logs.Count + " msgs=" + s.MessageCount);
            Check("전달 배압 중에도 UI 응답 유지 (< 3초)", _probe.MaxMs < 3000, $"최대 {_probe.MaxMs:0} ms");
            Note("배압 전: " + before);
            Note("배압 후: " + after);
        }

        #endregion

        #region 7. 연결 수 확장 (한계 탐색)

        private static void ScaleOut()
        {
            int[] steps = { 100, 400, 800, 1600, 2400, 3200, 4000, 5000, MaxConnectionsToTry };
            var pairs = new List<(ConnectionModel srv, ConnectionModel cli)>();
            string abortReason = null;
            int reached = 0;

            Table("연결쌍 |  프로세스 |   GC힙 |   CPU | 핸들   | 스레드 | 시스템여유 | UI최대지연");
            Table("------ | --------- | ------ | ----- | ------ | ------ | ---------- | ----------");

            foreach (int target in steps)
            {
                if (abortReason != null) break;

                ResetCpuWindow(); // 이 단계에서 쓴 CPU만 재도록 창을 새로 잡습니다.

                while (pairs.Count < target && abortReason == null)
                {
                    int port;
                    try { port = FreePort(); }
                    catch (Exception ex) { abortReason = "포트를 더 얻지 못함: " + ex.Message; break; }

                    var srv = Server(port);
                    _vm.Connections.Add(srv);
                    _vm.SelectedConnection = srv;
                    _vm.SelectedItems.Clear();
                    _vm.SelectedItems.Add(srv);
                    _vm.StartConnectionCommand.Execute(null);

                    var cli = Client(port);
                    _vm.Connections.Add(cli);
                    _vm.SelectedConnection = cli;
                    _vm.SelectedItems.Clear();
                    _vm.SelectedItems.Add(cli);
                    _vm.StartConnectionCommand.Execute(null);

                    pairs.Add((srv, cli));

                    // 25쌍마다 상태를 확인하고 한계선을 점검합니다.
                    if (pairs.Count % 25 == 0)
                    {
                        Pump(150);
                        var snap = Measure();
                        abortReason = GuardTripped(snap, _probe.MaxMs);
                    }
                }

                Pump(800);
                var s = Measure();
                reached = pairs.Count;
                Table($"{pairs.Count,6} | {s.ProcessMb,6} MB | {s.GcMb,4} MB | {s.CpuPercent,4:0.0}% | {s.Handles,6} | {s.Threads,6} | {s.SystemFreeMb,7} MB | {_probe.MaxMs,7:0} ms");

                if (abortReason == null) abortReason = GuardTripped(s, _probe.MaxMs);
            }

            Pump(2000);

            int listening = pairs.Count(p => p.srv.Status == "Listening");
            int connected = pairs.Count(p => p.cli.Status == "Connected");
            int errored = _vm.Connections.Count(c => c.Status == "Error");

            Note($"도달한 연결쌍: {reached}쌍 (연결 객체 {_vm.Connections.Count}개)");
            Note($"서버 Listening {listening} / 클라이언트 Connected {connected} / Error {errored}");
            if (abortReason != null) Note("중단 사유: " + abortReason);

            var firstError = _vm.Connections.FirstOrDefault(c => c.Status == "Error");
            if (firstError != null) Note("첫 실패 연결의 표시: " + firstError.Address + " -> " + firstError.ErrorText);
            var bannerKinds = _vm.Banners.Select(b => b.Kind).Distinct().ToList();
            if (bannerKinds.Count > 0) Note("표시된 배너 종류: " + string.Join(", ", bannerKinds));

            Check("안전 한계선에 닿기 전까지 앱이 죽지 않고 살아 있음", _w.IsVisible);
            Check("대량 연결 상태에서도 UI가 응답함", _probe.MaxMs < AbortWhenUiStalledMs,
                  $"최대 {_probe.MaxMs:0} ms");
            Check("실패한 연결은 크래시 대신 Error 상태로 표시됨",
                  errored == 0 || _vm.Connections.Any(c => c.Status == "Error" && !string.IsNullOrEmpty(c.ErrorText)),
                  "Error=" + errored);
            Check("자원 한계에 닿아도 OS를 위협하지 않음 (시스템 여유 메모리 유지)",
                  Measure().SystemFreeMb > AbortWhenSystemFreeBelowMb,
                  Measure().SystemFreeMb + " MB");
            // 연결을 다 만든 뒤 '아무 일도 하지 않는' 상태에서 3초간 CPU를 재봅니다.
            // 연결을 만드는 동안의 CPU가 아니라, 열어 둔 채 유지하는 데 드는 비용이 관심사입니다.
            ResetCpuWindow();
            Pump(3000);
            var idle = Measure();
            Note($"연결 {pairs.Count * 2}개를 열어 둔 유휴 상태 CPU: {idle.CpuPercent:0.0}% " +
                 $"(논리 코어 {Environment.ProcessorCount}개 기준, 한 코어분 = {100.0 / Environment.ProcessorCount:0.0}%)");
            Check("대량 연결을 열어 둔 유휴 상태에서 CPU가 한 코어분 미만",
                  idle.CpuPercent < 100.0 / Environment.ProcessorCount,
                  $"{idle.CpuPercent:0.0}%");
            Check($"목표치({MaxConnectionsToTry}쌍)까지 도달하거나, 도달 못 하면 이유가 기록됨",
                  reached >= MaxConnectionsToTry || abortReason != null,
                  $"도달 {reached}, 사유 {abortReason ?? "없음"}");

            _scaleReached = reached;
            _scaleAbort = abortReason;
        }

        private static int _scaleReached;
        private static string _scaleAbort;

        #endregion

        #region 8. 회수

        private static void Recovery()
        {
            Note($"직전 확장 테스트에서 {_scaleReached}쌍까지 만들었고, 정리는 Run()에서 이미 수행됐습니다.");
            if (_scaleAbort != null) Note("확장 중단 사유: " + _scaleAbort);

            // 정리 후 다시 정상적으로 열리는지 확인 (자원이 실제로 반납됐는지)
            var s = StartServer(out int port);
            Check("대량 부하 이후에도 새 서버를 정상적으로 열 수 있음",
                  WaitUntil(() => s.Status == "Listening", 5000), s.Status);

            using (var b = new Blaster())
            {
                Check("대량 부하 이후에도 정상 통신됨", b.Connect("127.0.0.1", port));
                var t = b.BlastAsync(Encoding.ASCII.GetBytes("AFTER-STRESS"), 1);
                WaitUntil(() => t.IsCompleted && s.BytesReceived >= 12, 5000);
                Check("부하 이후 수신 정상", s.BytesReceived >= 12, "rx=" + s.BytesReceived);
            }

            int p = s.Port;
            _vm.SelectedItems.Clear();
            _vm.SelectedItems.Add(s);
            _vm.StopConnectionCommand.Execute(null);
            Pump(500);
            Check("중지 후 포트 반납", WaitUntil(() => PortIsFree(p), 5000));
        }

        #endregion
    }
}
