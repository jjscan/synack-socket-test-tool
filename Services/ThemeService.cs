using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace SocketTestTool.Services
{
    /// <summary>
    /// 사용자가 고를 수 있는 테마입니다.
    /// </summary>
    public enum AppTheme
    {
        /// <summary>Windows의 앱 테마 설정을 따릅니다.</summary>
        System,
        Light,
        Dark
    }

    /// <summary>
    /// 라이트/다크 색상 사전을 실행 중에 바꿔 끼우는 서비스입니다.
    ///
    /// 동작 원리: App.xaml이 Themes/Light.xaml을 MergedDictionaries의 첫 항목으로,
    /// Themes/Fluent.xaml(스타일 정의)을 그다음으로 병합해 둡니다.
    /// 이 서비스는 첫 항목만 통째로 교체하며, 스타일들이 색을 DynamicResource로 참조하기 때문에
    /// 교체하는 즉시 열려 있는 모든 창에 반영됩니다.
    /// </summary>
    public static class ThemeService
    {
        #region Fields

        // 절대 pack URI를 씁니다. 상대 경로는 '진입 어셈블리' 기준으로 풀리기 때문에,
        // 이 코드가 다른 실행 파일(예: 테스트 하네스)에서 호출되면 사전을 찾지 못합니다.
        private const string LightSource = "pack://application:,,,/SocketTestTool;component/Themes/Light.xaml";
        private const string DarkSource = "pack://application:,,,/SocketTestTool;component/Themes/Dark.xaml";

        // 선택한 테마를 기억해 두는 파일입니다. 로그 폴더와 같이 실행 파일 옆에 둡니다.
        private static string SettingsPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme.json");

        #endregion

        #region Properties

        /// <summary>
        /// 현재 선택된 테마입니다. (System이면 실제 적용 색은 Windows 설정을 따릅니다.)
        /// </summary>
        public static AppTheme Current { get; private set; } = AppTheme.System;

        /// <summary>
        /// 실제로 지금 적용돼 있는 것이 다크인지 여부입니다.
        /// </summary>
        public static bool IsDarkApplied => Resolve(Current) == AppTheme.Dark;

        /// <summary>
        /// 테마가 바뀌었을 때 발생합니다. ViewModel이 메뉴 체크 상태를 갱신하는 데 씁니다.
        /// </summary>
        public static event Action ThemeChanged;

        #endregion

        #region Public Methods

        /// <summary>
        /// 저장된 설정을 읽어 시작 시 테마를 적용합니다.
        /// </summary>
        public static void Initialize()
        {
            Apply(LoadSaved());
        }

        /// <summary>
        /// 테마를 바꾸고 선택을 저장합니다.
        /// </summary>
        public static void Apply(AppTheme theme)
        {
            Current = theme;

            var dictionaries = Application.Current?.Resources.MergedDictionaries;
            if (dictionaries == null) return;

            string wanted = Resolve(theme) == AppTheme.Dark ? DarkSource : LightSource;

            // 기존 색상 사전을 찾아 같은 자리에서 교체합니다.
            // (자리를 유지해야 Fluent.xaml의 스타일보다 앞에 있게 되어 참조가 깨지지 않습니다.)
            var existing = dictionaries.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                 d.Source.OriginalString.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase)));

            var replacement = new ResourceDictionary { Source = new Uri(wanted, UriKind.Absolute) };

            if (existing != null)
            {
                int index = dictionaries.IndexOf(existing);
                dictionaries[index] = replacement;
            }
            else
            {
                dictionaries.Insert(0, replacement);
            }

            Save(theme);
            ThemeChanged?.Invoke();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// System 선택을 실제 Light/Dark 중 하나로 풀어 줍니다.
        /// </summary>
        private static AppTheme Resolve(AppTheme theme)
        {
            if (theme != AppTheme.System) return theme;
            return IsWindowsUsingDarkApps() ? AppTheme.Dark : AppTheme.Light;
        }

        /// <summary>
        /// Windows의 '앱 모드' 설정이 어두움인지 레지스트리에서 확인합니다.
        /// </summary>
        private static bool IsWindowsUsingDarkApps()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    // AppsUseLightTheme: 1이면 라이트, 0이면 다크입니다. 값이 없으면 라이트로 봅니다.
                    if (key?.GetValue("AppsUseLightTheme") is int useLight) return useLight == 0;
                }
            }
            catch (Exception)
            {
                // 레지스트리를 읽지 못해도 앱은 동작해야 하므로 라이트로 처리합니다.
            }

            return false;
        }

        private static AppTheme LoadSaved()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return AppTheme.System;

                var saved = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
                if (saved != null && saved.TryGetValue("theme", out string value) &&
                    Enum.TryParse(value, out AppTheme parsed))
                {
                    return parsed;
                }
            }
            catch (Exception)
            {
                // 설정이 깨져 있으면 기본값을 씁니다.
            }

            return AppTheme.System;
        }

        private static void Save(AppTheme theme)
        {
            try
            {
                File.WriteAllText(SettingsPath,
                    JsonConvert.SerializeObject(new Dictionary<string, string> { ["theme"] = theme.ToString() },
                                                Formatting.Indented));
            }
            catch (Exception)
            {
                // 저장 실패는 기능에 치명적이지 않으므로 무시합니다.
            }
        }

        #endregion
    }
}
