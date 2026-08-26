using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace SocketTestTool.Common
{
    /// <summary>
    /// ListView의 SelectedItems 속성을 ViewModel의 컬렉션 속성과 바인딩할 수 있도록 지원하는 Attached Behavior 클래스입니다.
    /// WPF의 ListView는 기본적으로 SelectedItems 속성에 대한 직접적인 TwoWay 바인딩을 지원하지 않기 때문에 이 클래스가 필요합니다.
    /// </summary>
    public static class ListViewBehavior
    {
        #region Attached Properties

        /// <summary>
        /// ViewModel의 컬렉션과 바인딩될 Attached Property를 정의합니다.
        /// </summary>
        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.RegisterAttached(
                "SelectedItems",
                typeof(IList),
                typeof(ListViewBehavior),
                new FrameworkPropertyMetadata(null, OnSelectedItemsChanged));

        /// <summary>
        /// XAML에서 Attached Property의 값을 가져오는 Getter 메서드입니다.
        /// </summary>
        public static IList GetSelectedItems(DependencyObject obj)
        {
            return (IList)obj.GetValue(SelectedItemsProperty);
        }

        /// <summary>
        /// XAML에서 Attached Property의 값을 설정하는 Setter 메서드입니다.
        /// </summary>
        public static void SetSelectedItems(DependencyObject obj, IList value)
        {
            obj.SetValue(SelectedItemsProperty, value);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// XAML에서 SelectedItems 속성이 설정될 때(즉, ViewModel의 컬렉션과 바인딩될 때) 호출되는 콜백 메서드입니다.
        /// </summary>
        private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Behavior가 ListView 컨트롤에 연결되었는지 확인합니다.
            if (d is ListView listView)
            {
                // 메모리 누수를 방지하기 위해 이전에 구독했던 이벤트를 먼저 제거합니다.
                listView.SelectionChanged -= ListView_SelectionChanged;
                // ListView의 선택이 변경될 때마다 우리가 만든 ListView_SelectionChanged 메서드가 호출되도록 이벤트를 구독합니다.
                listView.SelectionChanged += ListView_SelectionChanged;
            }
        }

        /// <summary>
        /// 실제 ListView의 선택 항목이 변경될 때마다 실행되는 이벤트 핸들러입니다.
        /// </summary>
        private static void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListView listView)
            {
                // XAML에서 바인딩된 ViewModel의 컬렉션을 가져옵니다.
                var targetCollection = GetSelectedItems(listView);
                if (targetCollection == null) return;

                // ViewModel의 컬렉션 인스턴스를 교체하는 대신, 내용물만 동기화합니다.
                // 이는 바인딩이 끊어지는 것을 방지하는 안정적인 방법입니다.
                targetCollection.Clear();
                foreach (var item in listView.SelectedItems)
                {
                    targetCollection.Add(item);
                }
            }
        }

        #endregion
    }
}