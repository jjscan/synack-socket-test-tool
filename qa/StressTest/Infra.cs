using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace Stress
{
    /// <summary>부하 테스트용 공용 기반: 기록, 자원 측정, 안전 한계선, UI 응답성 측정.</summary>
    internal static class Infra
    {
        #region 안전 한계선 (여기를 고치면 테스트 강도가 바뀝니다)

        /// <summary>시스템 여유 물리 메모리가 이보다 적어지면 즉시 중단합니다.</summary>
        public const long AbortWhenSystemFreeBelowMb = 4096;

        /// <summary>이 프로세스가 이보다 많은 메모리를 쓰면 중단합니다.</summary>
        public const long AbortWhenProcessMemoryAboveMb = 4096;

        /// <summary>핸들 수 상한. 소켓·이벤트 누수를 잡는 기준입니다.</summary>
        public const int AbortWhenHandlesAbove = 50000;

        /// <summary>스레드 수 상한.</summary>
        public const int AbortWhenThreadsAbove = 3000;

        /// <summary>UI가 이 시간 이상 응답하지 않으면 사실상 멈춘 것으로 봅니다.</summary>
        public const int AbortWhenUiStalledMs = 5000;

        /// <summary>연결 수 확장 테스트의 절대 상한. (동적 포트 범위가 약 16,384개입니다)</summary>
        public const int MaxConnectionsToTry = 6000;

        #endregion

        #region 기록

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
            detail = detail ?? "(null)";
            _index++;
            if (ok) _pass++;
            else { _fail++; Failures.Add($"[{_section}] {name}" + (detail.Length > 0 ? " :: " + detail : "")); }
            Report.AppendLine($"  {(ok ? "PASS" : "FAIL")}  #{_index,-3} {name}" + (detail.Length > 0 ? "  :: " + detail : ""));
            return ok;
        }

        public static void Note(string t) => Report.AppendLine("        " + t);
        public static void Table(string t) => Report.AppendLine("        " + t);
        public static int FailCount => _fail;

        public static void Write(string path)
        {
            Report.AppendLine();
            Report.AppendLine(new string('-', 64));
            if (_fail == 0) Report.AppendLine($"ALL PASS — {_pass}개 검사 전부 통과");
            else
            {
                Report.AppendLine($"{_fail} FAILURE(S) of {_pass + _fail} checks");
                foreach (var f in Failures) Report.AppendLine("  - " + f);
            }
            File.WriteAllText(path, Report.ToString(), Encoding.UTF8);
        }

        #endregion

        #region UI 펌핑

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
            while (DateTime.Now < end) { DoEvents(); Thread.Sleep(2); }
        }

        public static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            var end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                if (cond()) return true;
                DoEvents();
                Thread.Sleep(5);
            }
            return cond();
        }

        #endregion

        #region 자원 측정

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys;
            public ulong ullTotalPageFile, ullAvailPageFile;
            public ulong ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        /// <summary>시스템 전체의 여유 물리 메모리(MB).</summary>
        public static long SystemFreeMb()
        {
            var m = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref m) ? (long)(m.ullAvailPhys / 1024 / 1024) : -1;
        }

        public sealed class Snapshot
        {
            public long ProcessMb, SystemFreeMb, GcMb;
            public int Handles, Threads;
            /// <summary>직전 측정 이후의 평균 CPU 사용률(%). 논리 코어 전체를 100%로 봅니다.</summary>
            public double CpuPercent;
            public override string ToString()
                => $"프로세스 {ProcessMb,5} MB | GC힙 {GcMb,5} MB | CPU {CpuPercent,5:0.0}% | 핸들 {Handles,6} | 스레드 {Threads,4} | 시스템여유 {SystemFreeMb,6} MB";
        }

        private static DateTime _lastCpuAt = DateTime.UtcNow;
        private static TimeSpan _lastCpuTotal = TimeSpan.Zero;

        public static Snapshot Measure()
        {
            var p = Process.GetCurrentProcess();
            p.Refresh();

            // ResetCpuWindow() 이후 이 프로세스가 쓴 CPU 시간을, 흐른 실제 시간과 코어 수로 나눠 사용률을 냅니다.
            // 여기서 창을 초기화하지 않는 것이 중요합니다. 측정할 때마다 초기화하면
            // 바로 앞 측정 이후의 몇 밀리초만 재게 되어 값이 의미를 잃습니다.
            var now = DateTime.UtcNow;
            var total = p.TotalProcessorTime;
            double elapsedMs = (now - _lastCpuAt).TotalMilliseconds;
            double cpuMs = (total - _lastCpuTotal).TotalMilliseconds;
            double percent = elapsedMs <= 0 ? 0 : cpuMs / (elapsedMs * Environment.ProcessorCount) * 100.0;

            return new Snapshot
            {
                ProcessMb = p.PrivateMemorySize64 / 1024 / 1024,
                GcMb = GC.GetTotalMemory(false) / 1024 / 1024,
                Handles = p.HandleCount,
                Threads = p.Threads.Count,
                SystemFreeMb = SystemFreeMb(),
                CpuPercent = Math.Max(0, Math.Min(100, percent))
            };
        }

        /// <summary>CPU 사용률 기준선을 지금으로 다시 잡습니다.</summary>
        public static void ResetCpuWindow()
        {
            var p = Process.GetCurrentProcess();
            p.Refresh();
            _lastCpuAt = DateTime.UtcNow;
            _lastCpuTotal = p.TotalProcessorTime;
        }

        /// <summary>안전 한계선을 넘었으면 그 이유를 돌려줍니다. 안전하면 null.</summary>
        public static string GuardTripped(Snapshot s, double uiStallMs)
        {
            if (s.SystemFreeMb >= 0 && s.SystemFreeMb < AbortWhenSystemFreeBelowMb)
                return $"시스템 여유 메모리 {s.SystemFreeMb} MB < {AbortWhenSystemFreeBelowMb} MB";
            if (s.ProcessMb > AbortWhenProcessMemoryAboveMb)
                return $"프로세스 메모리 {s.ProcessMb} MB > {AbortWhenProcessMemoryAboveMb} MB";
            if (s.Handles > AbortWhenHandlesAbove)
                return $"핸들 {s.Handles} > {AbortWhenHandlesAbove}";
            if (s.Threads > AbortWhenThreadsAbove)
                return $"스레드 {s.Threads} > {AbortWhenThreadsAbove}";
            if (uiStallMs > AbortWhenUiStalledMs)
                return $"UI 응답 지연 {uiStallMs:0} ms > {AbortWhenUiStalledMs} ms";
            return null;
        }

        #endregion

        #region UI 응답성 측정

        /// <summary>
        /// 백그라운드 스레드에서 UI 디스패처에 작업을 던지고 처리될 때까지 걸린 시간을 잽니다.
        /// 소켓 처리가 UI 스레드를 잡아먹으면 이 값이 치솟습니다.
        /// </summary>
        public sealed class UiProbe : IDisposable
        {
            private readonly Dispatcher _dispatcher;
            private readonly Thread _thread;
            private volatile bool _running = true;
            private long _maxMs, _samples, _totalMs;

            public UiProbe(Dispatcher dispatcher)
            {
                _dispatcher = dispatcher;
                _thread = new Thread(Loop) { IsBackground = true, Name = "ui-probe" };
                _thread.Start();
            }

            private void Loop()
            {
                while (_running)
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var op = _dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                        if (!op.Task.Wait(AbortWhenUiStalledMs * 2)) sw.Stop();
                    }
                    catch (Exception) { }
                    sw.Stop();

                    Interlocked.Add(ref _totalMs, sw.ElapsedMilliseconds);
                    Interlocked.Increment(ref _samples);
                    long cur = Interlocked.Read(ref _maxMs);
                    if (sw.ElapsedMilliseconds > cur) Interlocked.Exchange(ref _maxMs, sw.ElapsedMilliseconds);

                    Thread.Sleep(20);
                }
            }

            public double MaxMs => Interlocked.Read(ref _maxMs);
            public double AvgMs
            {
                get
                {
                    long n = Interlocked.Read(ref _samples);
                    return n == 0 ? 0 : (double)Interlocked.Read(ref _totalMs) / n;
                }
            }

            public void Reset()
            {
                Interlocked.Exchange(ref _maxMs, 0);
                Interlocked.Exchange(ref _samples, 0);
                Interlocked.Exchange(ref _totalMs, 0);
            }

            public void Dispose() => _running = false;
        }

        #endregion

        #region 소켓 도우미

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
            try { var l = new TcpListener(IPAddress.Loopback, port); l.Start(); l.Stop(); return true; }
            catch (SocketException) { return false; }
        }

        /// <summary>앱 밖에서 도는 부하 발생용 클라이언트.</summary>
        public sealed class Blaster : IDisposable
        {
            private readonly TcpClient _client = new TcpClient();
            private long _rx;
            private bool _stopped;

            public bool Connect(string ip, int port, int timeoutMs = 5000)
            {
                try
                {
                    var t = _client.ConnectAsync(ip, port);
                    var end = DateTime.Now.AddMilliseconds(timeoutMs);
                    while (!t.IsCompleted && DateTime.Now < end) { DoEvents(); Thread.Sleep(5); }
                    if (!t.IsCompletedSuccessfully) return false;
                    Drain();
                    return true;
                }
                catch (Exception) { return false; }
            }

            private async void Drain()
            {
                try
                {
                    var s = _client.GetStream();
                    var buf = new byte[65536];
                    while (!_stopped)
                    {
                        int n = await s.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                        if (n == 0) break;
                        Interlocked.Add(ref _rx, n);
                    }
                }
                catch (Exception) { }
            }

            public long BytesReceived => Interlocked.Read(ref _rx);

            /// <summary>백그라운드에서 정해진 횟수만큼 쏟아붓습니다.</summary>
            public System.Threading.Tasks.Task BlastAsync(byte[] payload, int count, int gapMs = 0)
            {
                return System.Threading.Tasks.Task.Run(() =>
                {
                    var s = _client.GetStream();
                    for (int i = 0; i < count && !_stopped; i++)
                    {
                        s.Write(payload, 0, payload.Length);
                        if (gapMs > 0) Thread.Sleep(gapMs);
                    }
                    s.Flush();
                });
            }

            /// <summary>
            /// 침묵 없이 계속 스트리밍합니다. Dispose()나 별도 stopFlag로 멈출 때까지 멈추지 않습니다.
            /// 서버의 수신 누적 상한을 검증하는 데 씁니다.
            /// </summary>
            public System.Threading.Tasks.Task FloodAsync(byte[] payload, Func<bool> keepGoing)
            {
                return System.Threading.Tasks.Task.Run(() =>
                {
                    var s = _client.GetStream();
                    try
                    {
                        while (!_stopped && keepGoing())
                        {
                            s.Write(payload, 0, payload.Length);
                        }
                    }
                    catch (Exception) { /* 상대가 끊거나 Dispose되면 정상 종료 */ }
                });
            }

            public void Dispose() { _stopped = true; try { _client.Close(); } catch (Exception) { } }
        }

        #endregion
    }
}
