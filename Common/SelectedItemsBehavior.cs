using Microsoft.Xaml.Behaviors;
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace SocketTestTool.Common
{
    /// <summary>
    /// ListView의 SelectedItems 속성(읽기 전용)을 ViewModel의 컬렉션과 동기화하기 위한 Behavior 클래스입니다.
    /// 이 Behavior를 사용하면 코드 비하인드 없이 순수 XAML 바인딩만으로 다중 선택 목록을 ViewModel에 전달할 수 있습니다.
    /// </summary>
    public class SelectedItemsBehavior : Behavior<ListView>
    {
        #region Dependency Properties

        /// <summary>
        /// ViewModel의 컬렉션과 바인딩될 SelectedItems 종속성 속성을 정의합니다.
        /// </summary>
        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.Register(nameof(SelectedItems), typeof(IList), typeof(SelectedItemsBehavior), new PropertyMetadata(null));

        /// <summary>
        /// ViewModel의 ObservableCollection<object>와 바인딩되는 속성입니다.
        /// </summary>
        public IList SelectedItems
        {
            get { return (IList)GetValue(SelectedItemsProperty); }
            set { SetValue(SelectedItemsProperty, value); }
        }

        #endregion

        #region Behavior Lifecycle

        /// <summary>
        /// 이 Behavior가 ListView에 연결될 때 호출됩니다.
        /// </summary>
        protected override void OnAttached()
        {
            base.OnAttached();
            // ListView의 SelectionChanged 이벤트가 발생할 때마다 OnSelectionChanged 메서드를 호출하도록 구독합니다.
            AssociatedObject.SelectionChanged += OnSelectionChanged;
        }

        /// <summary>
        /// 이 Behavior가 ListView에서 분리될 때 호출됩니다. (예: 창이 닫힐 때)
        /// </summary>
        protected override void OnDetaching()
        {
            base.OnDetaching();
            // 메모리 누수를 방지하기 위해 구독했던 이벤트를 해지합니다.
            AssociatedObject.SelectionChanged -= OnSelectionChanged;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// ListView의 선택 항목이 변경될 때마다 실행됩니다.
        /// </summary>
        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ViewModel과 바인딩된 컬렉션이 유효한지 확인합니다.
            if (SelectedItems == null) return;

            // ViewModel 컬렉션의 내용을 현재 ListView에서 선택된 항목들과 똑같이 동기화합니다.
            SelectedItems.Clear();
            if (AssociatedObject.SelectedItems != null)
            {
                foreach (var item in AssociatedObject.SelectedItems)
                {
                    SelectedItems.Add(item);
                }
            }
        }

        #endregion
    }
}