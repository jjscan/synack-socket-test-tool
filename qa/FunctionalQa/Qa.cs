using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace FullQa
{
    /// <summary>QA 하네스의 공용 기반: 결과 기록, UI 펌핑, 소켓 도우미, 색 계산, 화면 캡처.</summary>
    internal static class Qa
    {
        #region Reporting

        private static readonly StringBuilder Report = new StringBuilder();
        private static readonly List<string> Failures = new List<string>();
        private static int _pass, _fail, _index;
        private static string _section = "";

        public static void Section(string title)
        {
            _section = title;
            Report.AppendLine();
            Report.AppendLine("== " + title + " ==");
        }

        public static bool Check(string name, bool ok, string detail = "")
        {
            // detail에 null이 올 수 있으므로(예: 비어 있어야 정상인 값) 방어합니다.
            detail = detail ?? "(null)";

            _index++;
            if (ok) _pass++;
            else { _fail++; Failures.Add($"[{_section}] {name}" + (detail.Length > 0 ? " :: " + detail : "")); }

            Report.AppendLine($"  {(ok ? "PASS" : "FAIL")}  #{_index,-3} {name}" + (detail.Length > 0 ? "  :: " + detail : ""));
            return ok;
        }

        public static void Note(string text) => Report.AppendLine("        " + text);

        public static int FailCount => _fail;

        public static void Write(string path)
        {
            Report.AppendLine();
            Report.AppendLine(new string('-', 60));
            if (_fail == 0)
            {
                Report.AppendLine($"ALL PASS — {_pass}개 검사 전부 통과");
            }
            else
            {
                Report.AppendLine($"{_fail} FAILURE(S) of {_pass + _fail} checks");
                foreach (var f in Failures) Report.AppendLine("  - " + f);
            }
            File.WriteAllText(path, Report.ToString(), Encoding.UTF8);
        }

        #endregion

        #region Dispatcher

        public static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        public static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { DoEvents(); Thread.Sleep(5); }
        }

        public static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            var end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                if (cond()) return true;
                DoEvents();
                Thread.Sleep(10);
            }
            return cond();
        }

        #endregion

        #region Sockets

        public static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        public static bool PortIsFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch (SocketException) { return false; }
        }

        public static string Hex(byte[] data)
        {
            if (data == null) return "";
            return string.Join(" ", data.Select(b => b.ToString("X2")));
        }

        /// <summary>앱 밖에서 도는 단순 클라이언트. 앱이 연 서버에 사람처럼 붙습니다.</summary>
        public sealed class RawClient : IDisposable
        {
            private readonly TcpClient _client = new TcpClient();
            private readonly List<byte> _rx = new List<byte>();
            private readonly object _lock = new object();
            private bool _stopped;

            public bool Connect(string ip, int port, int timeoutMs = 3000)
            {
                try
                {
                    var task = _client.ConnectAsync(ip, port);
                    var end = DateTime.Now.AddMilliseconds(timeoutMs);
                    while (!task.IsCompleted && DateTime.Now < end) { DoEvents(); Thread.Sleep(10); }
                    if (!task.IsCompletedSuccessfully) return false;
                    Read();
                    return true;
                }
                catch (Exception) { return false; }
            }

            private async void Read()
            {
                try
                {
                    var s = _client.GetStream();
                    var buf = new byte[8192];
                    while (!_stopped)
                    {
                        int n = await s.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                        if (n == 0) break;
                        lock (_lock) { for (int i = 0; i < n; i++) _rx.Add(buf[i]); }
                    }
                }
                catch (Exception) { }
            }

            public void Send(byte[] d) => _client.GetStream().Write(d, 0, d.Length);
            public void SendAscii(string s) => Send(Encoding.ASCII.GetBytes(s));
            public string TextAscii() { lock (_lock) return Encoding.ASCII.GetString(_rx.ToArray()); }
            public int Count { get { lock (_lock) return _rx.Count; } }
            public void Clear() { lock (_lock) _rx.Clear(); }
            public void Dispose() { _stopped = true; try { _client.Close(); } catch (Exception) { } }
        }

        /// <summary>자동 전달의 도착지로 쓰는 수집 서버. 켜고 끌 수 있습니다.</summary>
        public sealed class SinkServer : IDisposable
        {
            private TcpListener _listener;
            private readonly List<byte> _rx = new List<byte>();
            private readonly object _lock = new object();
            private bool _stopped;

            public int Port { get; }

            public SinkServer(bool startNow = true)
            {
                Port = FreePort();
                if (startNow) Start();
            }

            public void Start()
            {
                _stopped = false;
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();
                Accept();
            }

            private async void Accept()
            {
                try
                {
                    while (!_stopped)
                    {
                        var c = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        ReadFrom(c);
                    }
                }
                catch (Exception) { }
            }

            private async void ReadFrom(TcpClient c)
            {
                try
                {
                    var s = c.GetStream();
                    var buf = new byte[8192];
                    while (!_stopped)
                    {
                        int n = await s.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                        if (n == 0) break;
                        lock (_lock) { for (int i = 0; i < n; i++) _rx.Add(buf[i]); }
                    }
                }
                catch (Exception) { }
            }

            public string TextAscii() { lock (_lock) return Encoding.ASCII.GetString(_rx.ToArray()); }
            public void Clear() { lock (_lock) _rx.Clear(); }
            public void Stop() { _stopped = true; try { _listener?.Stop(); } catch (Exception) { } }
            public void Dispose() => Stop();
        }

        #endregion

        #region Reflection (x:Name 필드는 internal이라 리플렉션으로 접근합니다)

        private const BindingFlags Any =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static T Field<T>(object target, string name) where T : class
            => target.GetType().GetField(name, Any)?.GetValue(target) as T;

        public static void Invoke(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, Any);
            try { m.Invoke(target, args); }
            catch (TargetInvocationException tie) when (tie.InnerException is InvalidOperationException)
            {
                // 비모달 창에서는 DialogResult를 설정할 수 없습니다.
                // 결과 속성은 그 직전에 모두 채워지므로 이 예외는 무시해도 됩니다.
            }
        }

        #endregion

        #region 저장소 위치

        /// <summary>
        /// 이 하네스는 저장소 안(qa/…/bin/…)에서 실행되므로,
        /// SocketTestTool.csproj가 보일 때까지 상위로 올라가 저장소 루트를 찾습니다.
        /// 절대 경로를 박아 두면 저장소를 옮기는 순간 깨집니다.
        /// </summary>
        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SocketTestTool.csproj"))) return dir.FullName;
                dir = dir.Parent;
            }
            return AppContext.BaseDirectory;
        }

        #endregion

        #region Colors

        public static double Luminance(Color c)
        {
            Func<double, double> f = v =>
            {
                v /= 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            };
            return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
        }

        public static double Contrast(Color fg, Color bg)
        {
            double a = Luminance(fg), b = Luminance(bg);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                yield return c;
                foreach (var d in Descendants(c)) yield return d;
            }
        }

        public static T Find<T>(DependencyObject root, Func<T, bool> match = null) where T : DependencyObject
        {
            foreach (var d in Descendants(root))
                if (d is T t && (match == null || match(t))) return t;
            return null;
        }

        public static Color? SolidBackground(DependencyObject e)
        {
            var cur = e;
            while (cur != null)
            {
                if (cur is System.Windows.Controls.Border b && b.Background is SolidColorBrush s && s.Color.A > 200) return s.Color;
                if (cur is System.Windows.Controls.Control c && c.Background is SolidColorBrush cb && cb.Color.A > 200) return cb.Color;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return null;
        }

        #endregion

        #region Capture

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);

        public static void CaptureScreen(string name)
        {
            try
            {
                int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
                using (var bmp = new System.Drawing.Bitmap(w, h))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(w, h));
                    string file = Path.Combine(Path.GetTempPath(), "fullqa-" + name + ".png");
                    bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                    Note("shot: " + file);
                }
            }
            catch (Exception ex) { Note("(capture failed: " + ex.Message + ")"); }
        }

        #endregion
    }
}
