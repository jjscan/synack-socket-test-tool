using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SocketTestTool.Services
{
    /// <summary>
    /// 특정 TCP 포트를 어떤 프로세스가 점유하고 있는지 조회하는 서비스입니다.
    /// 포트 바인딩 실패 배너의 '누가 쓰는지 보기'(목업 1f)에 사용됩니다.
    /// </summary>
    public static class PortOwnerLookup
    {
        #region Public Methods

        /// <summary>
        /// 지정한 포트를 LISTENING 상태로 점유 중인 프로세스를 찾아 사람이 읽을 문장으로 돌려줍니다.
        /// </summary>
        /// <param name="port">조회할 TCP 포트 번호입니다.</param>
        /// <returns>예: "nginx.exe (PID 4812)" / 찾지 못하면 안내 문장</returns>
        public static async Task<string> DescribeOwnerAsync(int port)
        {
            var (owner, problem) = await LookupAsync(port).ConfigureAwait(false);
            return owner != null ? $"포트 {port} 사용 중: {owner}" : problem;
        }

        /// <summary>
        /// 점유 프로세스를 "nginx.exe (PID 4812)" 처럼 짧게만 돌려줍니다. 찾지 못하면 null입니다.
        /// 이미 문맥이 있는 곳(연결 확인 결과처럼)에서 문장이 겹치지 않게 하려고 나눠 두었습니다.
        /// </summary>
        public static async Task<string?> FindOwnerShortAsync(int port)
        {
            var (owner, _) = await LookupAsync(port).ConfigureAwait(false);
            return owner;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 포트를 LISTENING 중인 프로세스를 찾습니다.
        /// 찾으면 owner에 "이름.exe (PID n)"가, 못 찾으면 problem에 그 이유가 담깁니다.
        /// </summary>
        private static async Task<(string? owner, string problem)> LookupAsync(int port)
        {
            try
            {
                // netstat -ano의 출력에서 로컬 주소가 :port로 끝나고 LISTENING인 줄의 PID를 찾습니다.
                // (소유 PID를 직접 얻으려면 GetExtendedTcpTable P/Invoke가 필요한데,
                //  진단용 1회 조회에는 netstat 파싱이 훨씬 가볍습니다.)
                string output = await RunNetstatAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(output)) return (null, "포트 사용 정보를 읽지 못했습니다.");

                var match = Regex.Match(
                    output,
                    @"^\s*TCP\s+\S+:" + port + @"\s+\S+\s+LISTENING\s+(\d+)\s*$",
                    RegexOptions.Multiline);

                if (!match.Success)
                {
                    return (null, $"포트 {port}을(를) LISTENING 중인 프로세스를 찾지 못했습니다.");
                }

                if (!int.TryParse(match.Groups[1].Value, out int pid))
                {
                    return (null, $"포트 {port} 점유 프로세스의 PID를 해석하지 못했습니다.");
                }

                string processName;
                try
                {
                    processName = Process.GetProcessById(pid).ProcessName + ".exe";
                }
                catch (Exception)
                {
                    // 조회 직후 프로세스가 종료된 경우 등
                    processName = "(알 수 없는 프로세스)";
                }

                return ($"{processName} (PID {pid})", string.Empty);
            }
            catch (Exception ex)
            {
                return (null, $"포트 사용 정보를 읽지 못했습니다: {ex.Message}");
            }
        }

        /// <summary>
        /// netstat -ano를 백그라운드에서 실행하고 표준 출력을 문자열로 돌려줍니다.
        /// </summary>
        private static Task<string> RunNetstatAsync()
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo("netstat", "-ano")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return string.Empty;

                    string output = process.StandardOutput.ReadToEnd();

                    // 응답이 없더라도 UI가 묶이지 않도록 대기 시간을 제한합니다.
                    if (!process.WaitForExit(5000))
                    {
                        try { process.Kill(); } catch (Exception) { }
                    }

                    return output;
                }
            });
        }

        #endregion
    }
}
