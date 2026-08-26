using SocketTestTool.Models;
using System;
using System.Collections.Generic;
using System.IO;
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

        #endregion

        #region Public Static Methods

        /// <summary>
        /// 지정된 연결에 대한 로그 파일 쓰기를 초기화합니다.
        /// 사용자가 지정한 경로가 없으면 기본 경로에 로그 파일을 생성합니다.
        /// </summary>
        /// <param name="conn">로그를 초기화할 ConnectionModel 객체입니다.</param>
        public static void Initialize(ConnectionModel conn)
        {
            lock (_lock)
            {
                // 이미 해당 연결에 대한 파일 쓰기 객체가 있다면 중복 생성을 방지합니다.
                if (_writers.ContainsKey(conn.Id)) return;

                string filePath;

                // 사용자가 파일 경로를 직접 지정했는지 확인합니다.
                if (!string.IsNullOrWhiteSpace(conn.LogFilePath))
                {
                    filePath = conn.LogFilePath;
                    // 사용자가 지정한 경로의 폴더가 없을 경우를 대비하여 폴더를 생성합니다.
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                }
                else
                {
                    // 기본 경로 생성 로직 (실행파일위치\Logs\...)
                    string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    Directory.CreateDirectory(logDir);
                    string fileName = $"{conn.Type}_{conn.Address.Replace(":", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                    filePath = Path.Combine(logDir, fileName);
                }

                // UTF-8 인코딩으로 파일을 열고, 기존 내용에 이어쓰기 모드(append: true)로 설정합니다.
                var writer = new StreamWriter(filePath, append: true, Encoding.UTF8)
                {
                    // AutoFlush를 true로 설정하여 WriteLine 호출 즉시 파일에 쓰도록 합니다. (성능보다 안정성 우선)
                    AutoFlush = true
                };
                _writers.Add(conn.Id, writer);
            }
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