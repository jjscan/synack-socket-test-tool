using System.Configuration;
using System.Data;
using System.Windows;

namespace SocketTestTool
{
    /// <summary>
    /// WPF 애플리케이션의 주 진입점 및 전역 관리를 담당하는 클래스입니다.
    /// App.xaml과 상호 작용합니다.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 앱이 시작될 때 저장된 테마(라이트/다크/시스템 설정)를 적용합니다.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Services.ThemeService.Initialize();
        }
    }
}