using SocketTestTool.Models;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SocketTestTool.Views
{
    /// <summary>
    /// 릴리스 기록을 좌측 버전 목록 + 우측 상세 카드로 보여 주는 창입니다. (목업 1g)
    /// </summary>
    public partial class VersionHistoryWindow : Window
    {
        public VersionHistoryWindow()
        {
            InitializeComponent();

            VersionListBox.ItemsSource = ReleaseHistory.All;

            // 처음에는 현재 버전(없으면 첫 항목)을 펼쳐 둡니다.
            VersionListBox.SelectedItem = ReleaseHistory.All.FirstOrDefault(r => r.IsCurrent)
                                          ?? ReleaseHistory.All.FirstOrDefault();
        }

        /// <summary>
        /// 좌측에서 버전을 고르면 우측 상세를 그 버전으로 바꿉니다.
        /// </summary>
        private void VersionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionListBox.SelectedItem is ReleaseNote note)
            {
                // 우측 상세 영역은 선택된 ReleaseNote를 DataContext로 삼습니다.
                DataContext = note;
            }
        }
    }
}
