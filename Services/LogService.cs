using SocketTestTool.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SocketTestTool.Services
{
    /// <summary>
    /// 로그 파일의 생성, 쓰기, 닫기를 담당하는 정적 서비스 클래스입니다.
    /// 여러 스레드에서 동시에 접근해도 안전하도록(Thread-Safe) 설계되었습니다.
    /// </summary>
    public static class LogService
    {
        #region Fields

        // 여러 스레드가 _writers 딕셔너리에 동시에 접근하는 것을 방지하기 위한 잠금(lock) 객체입니다.
        private static readonly object _lock = new object();
        // 각 연결(Connection)의 고유 ID와 파일 쓰기(StreamWriter) 객체를 매핑하는 딕셔셔너리입니다.
        private static readonly Dictionary<string, StreamWriter> _writers = new Dictionary<string, StreamWriter>();

        // 로그를 절대 기록하면 안 되는 보호 위치들입니다. 정규화된 전체 경로 기준으로 검사합니다.
        //
        // [보안] 이 앱은 관리자 권한으로 실행되고, 세션 파일(.json)에 담긴 LogFilePath는
        // 신뢰할 수 없는 값일 수 있습니다(악의적 세션을 열면 지정된 경로에 파일이 생성/추가됨).
        // 시스템 폴더나 시작프로그램 폴더로의 쓰기를 막아, 관리자 권한을 악용한
        // 권한 상승·자동 실행 지속성 확보를 차단합니다. 이 위치들은 '로그' 대상으로 쓸 이유가 없으므로
        // 정상 기능에는 영향이 없습니다.
        private static readonly string[] _protectedRoots = BuildProtectedRoots();

        private static string[] BuildProtectedRoots()
        {
            var folders = new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.SystemX86,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData,   // ProgramData
                Environment.SpecialFolder.Startup,                 // 현재 사용자 시작프로그램
                Environment.SpecialFolder.CommonStartup,           // 모든 사용자 시작프로그램 (관리자 필요)
                Environment.SpecialFolder.StartMenu,
                Environment.SpecialFolder.CommonStartMenu,
                Environment.SpecialFolder.Programs,
                Environment.SpecialFolder.CommonPrograms,
            };

            return folders
                .Select(f => Environment.GetFolderPath(f))
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        #endregion

        #region Public Static Methods

        /// <summary>
        /// 지정된 연결에 대한 로그 파일 쓰기를 초기화합니다.
        /// 사용자가 지정한 경로가 없으면 기본 경로에 로그 파일을 생성합니다.
        /// </summary>
        /// <param name="conn">로그를 초기화할 ConnectionModel 객체입니다.</param>
        /// <returns>로그 파일을 성공적으로 열었으면 true, 경로가 거부되었거나 열지 못했으면 false입니다.</returns>
        public static bool Initialize(ConnectionModel conn)
        {
            lock (_lock)
            {
                // 이미 해당 연결에 대한 파일 쓰기 객체가 있다면 중복 생성을 방지합니다.
                if (_writers.ContainsKey(conn.Id)) return true;

                try
                {
                    string filePath = ResolveLogPath(conn);

                    // [보안] 보호 위치(시스템·시작프로그램 등)로의 쓰기는 거부합니다.
                    if (IsProtectedLocation(filePath))
                    {
                        return false;
                    }

                    // 지정한 경로의 폴더가 없으면 만듭니다. (경로에 폴더 부분이 있을 때만)
                    string dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    // UTF-8 인코딩으로 파일을 열고, 기존 내용에 이어쓰기 모드(append: true)로 설정합니다.
                    var writer = new StreamWriter(filePath, append: true, Encoding.UTF8)
                    {
                        // AutoFlush를 true로 설정하여 WriteLine 호출 즉시 파일에 쓰도록 합니다. (성능보다 안정성 우선)
                        AutoFlush = true
                    };
                    _writers.Add(conn.Id, writer);
                    return true;
                }
                catch (Exception)
                {
                    // 잘못된 경로·권한 부족 등으로 파일을 열지 못하면 로그를 끄고 계속 진행합니다.
                    // 여기서 예외가 새어 나가면 연결 시작 자체가 실패하므로 반드시 삼킵니다.
                    return false;
                }
            }
        }

        /// <summary>
        /// 연결의 LogFilePath를 실제 사용할 전체 경로로 정규화합니다.
        /// 경로가 비어 있으면 실행 파일 옆 Logs 폴더에 자동 파일명을 만듭니다.
        /// </summary>
        private static string ResolveLogPath(ConnectionModel conn)
        {
            if (!string.IsNullOrWhiteSpace(conn.LogFilePath))
            {
                // 상대 경로·..\ 등을 흡수하도록 전체 경로로 정규화합니다.
                return Path.GetFullPath(conn.LogFilePath);
            }

            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            string address = conn.Address ?? "unknown";
            string fileName = $"{conn.Type}_{address.Replace(":", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            return Path.GetFullPath(Path.Combine(logDir, fileName));
        }

        /// <summary>
        /// 주어진 전체 경로가 보호 위치(시스템·시작프로그램 등) 안에 있는지 검사합니다.
        /// </summary>
        private static bool IsProtectedLocation(string fullPath)
        {
            string normalized = Path.TrimEndingDirectorySeparator(fullPath);

            foreach (var root in _protectedRoots)
            {
                // 정확히 그 폴더이거나, 그 폴더 하위 경로이면 거부합니다.
                // 구분자까지 비교해 "C:\WindowsApps"가 "C:\Windows"에 걸리는 오탐을 막습니다.
                if (normalized.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
                if (normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// 이 경로에 로그를 기록할 수 있는지 미리 확인합니다. (UI에서 경고를 띄우기 위한 용도)
        /// </summary>
        public static bool IsPathAllowed(string logFilePath)
        {
            if (string.IsNullOrWhiteSpace(logFilePath)) return true; // 비어 있으면 기본 폴더를 쓰므로 허용
            try { return !IsProtectedLocation(Path.GetFullPath(logFilePath)); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 지정된 연결 ID에 해당하는 로그 파일에 로그 항목을 기록합니다.
        /// </summary>
        /// <param name="connectionId">로그를 기록할 연결의 고유 ID입니다.</param>
        /// <param name="entry">기록할 LogEntry 객체입니다.</param>
        public static void Write(string connectionId, LogEntry entry)
        {
            lock (_lock)
            {
                // 딕셔너리에서 해당 연결의 StreamWriter를 찾아 로그를 기록합니다.
                if (_writers.TryGetValue(connectionId, out var writer))
                {
                    writer.WriteLine(entry.DisplayMessage);
                }
            }
        }

        /// <summary>
        /// 지정된 연결 ID에 해당하는 로그 파일을 닫고 리소스를 해제합니다.
        /// </summary>
        /// <param name="connectionId">닫을 연결의 고유 ID입니다.</param>
        public static void Close(string connectionId)
        {
            lock (_lock)
            {
                if (_writers.TryGetValue(connectionId, out var writer))
                {
                    writer.Close(); // StreamWriter를 닫아 파일 핸들을 해제합니다.
                    _writers.Remove(connectionId); // 딕셔너리에서 제거합니다.
                }
            }
        }

        #endregion
    }
}