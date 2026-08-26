using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SocketTestTool.Models
{
    /// <summary>
    /// 알림 배너의 심각도입니다.
    /// </summary>
    public enum BannerSeverity { Error, Warning, Success }

    /// <summary>
    /// 창 안에 인라인으로 표시되는 알림 배너 한 건입니다.
    /// 기존에 MessageBox로 띄우던 실패 알림을 대체합니다.
    /// (목업 1f - Failure states)
    /// </summary>
    public class BannerItem : INotifyPropertyChanged
    {
        #region Properties

        /// <summary>
        /// 배너의 심각도입니다. 색상 결정에 쓰입니다.
        /// </summary>
        public BannerSeverity Severity { get; set; } = BannerSeverity.Error;

        /// <summary>
        /// 굵게 표시되는 한 줄 제목입니다. (예: "포트 502를 열 수 없습니다 — Cannot bind 0.0.0.0:502")
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 제목 아래에 표시되는 설명입니다.
        /// </summary>
        public string Detail { get; set; }

        /// <summary>
        /// 개발자용 원인 문자열입니다. (예: "SocketException 10048 · EADDRINUSE")
        /// </summary>
        public string TechnicalDetail { get; set; }

        private string _statusNote;
        /// <summary>
        /// 배너 안에서 동작 결과를 알리는 짧은 메모입니다. (예: 포트 점유 프로세스 조회 결과)
        /// </summary>
        public string StatusNote
        {
            get => _statusNote;
            set { if (_statusNote != value) { _statusNote = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 이 배너를 만들어 낸 연결의 고유 ID입니다. 같은 연결의 배너를 갱신·제거할 때 씁니다.
        /// </summary>
        public string ConnectionId { get; set; }

        /// <summary>
        /// 같은 종류의 배너가 중복 쌓이지 않도록 구분하는 키입니다. (예: "bind-failed")
        /// </summary>
        public string Kind { get; set; }

        #endregion

        #region Actions

        public string PrimaryActionText { get; set; }
        public ICommand PrimaryActionCommand { get; set; }

        public string SecondaryActionText { get; set; }
        public ICommand SecondaryActionCommand { get; set; }

        public string TertiaryActionText { get; set; }
        public ICommand TertiaryActionCommand { get; set; }

        /// <summary>
        /// 이 배너를 닫는 커맨드입니다. MainViewModel이 채워 넣습니다.
        /// </summary>
        public ICommand DismissCommand { get; set; }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        #endregion
    }
}
