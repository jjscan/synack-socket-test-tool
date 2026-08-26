using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SocketTestTool.Services
{
    /// <summary>
    /// 최근에 저장하거나 불러온 세션 파일 목록을 기억하는 서비스입니다.
    /// 빈 상태 화면(목업 1e)의 '최근:' 목록에 사용됩니다.
    /// </summary>
    public static class RecentSessionService
    {
        #region Constants

        /// <summary>
        /// 목록에 유지할 최대 개수입니다.
        /// </summary>
        private const int MaxEntries = 5;

        #endregion

        #region Fields

        private static readonly object _lock = new object();

        // 로그 폴더와 같은 규칙으로 실행 파일 옆에 보관합니다.
        private static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recent-sessions.json");

        #endregion

        #region Public Methods

        /// <summary>
        /// 최근 세션 파일 경로 목록을 최신순으로 반환합니다.
        /// 이미 지워진 파일은 목록에서 제외합니다.
        /// </summary>
        public static List<string> Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(FilePath)) return new List<string>();

                    var paths = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(FilePath));
                    if (paths == null) return new List<string>();

                    // 파일이 사라진 항목은 보여 줄 이유가 없으므로 걸러 냅니다.
                    return paths.Where(File.Exists).Take(MaxEntries).ToList();
                }
                catch (Exception)
                {
                    // 목록이 깨져 있어도 앱 동작에는 지장이 없으므로 조용히 빈 목록을 씁니다.
                    return new List<string>();
                }
            }
        }

        /// <summary>
        /// 세션 파일을 목록 맨 앞에 추가합니다. 이미 있으면 위로 끌어올립니다.
        /// </summary>
        public static void Add(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            lock (_lock)
            {
                try
                {
                    var paths = File.Exists(FilePath)
                        ? (JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(FilePath)) ?? new List<string>())
                        : new List<string>();

                    paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                    paths.Insert(0, path);

                    if (paths.Count > MaxEntries) paths = paths.Take(MaxEntries).ToList();

                    File.WriteAllText(FilePath, JsonConvert.SerializeObject(paths, Formatting.Indented));
                }
                catch (Exception)
                {
                    // 기록 실패는 기능에 치명적이지 않으므로 무시합니다.
                }
            }
        }

        #endregion
    }
}
