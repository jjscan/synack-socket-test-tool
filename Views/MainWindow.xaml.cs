using SocketTestTool.Common;
using SocketTestTool.Models;
using SocketTestTool.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace SocketTestTool
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel();

            //Loaded 이벤트를 구독하여, 윈도우가 화면에 완전히 그려진 후 UpdateCommandStates()를 한 번 호출
            Loaded += (sender, e) =>
            {
                if (this.DataContext is MainViewModel vm)
                {
                    vm.UpdateCommandStates();
                }
            };
        }

        /// <summary>
        /// 현재 DataContext를 MainViewModel로 얻습니다. (없으면 null)
        /// </summary>
        private MainViewModel? ViewModel => this.DataContext as MainViewModel;

        private void ConnectionListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 자동 스크롤을 위한 이벤트 구독/해지 로직입니다.
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is ConnectionModel oldConn)
            {
                oldConn.Logs.CollectionChanged -= Logs_CollectionChanged;
            }
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ConnectionModel newConn)
            {
                newConn.Logs.CollectionChanged += Logs_CollectionChanged;
            }

            // 선택이 바뀌면 로그 목록도 통째로 바뀌므로 검색 필터와 개수를 다시 적용합니다.
            Dispatcher.BeginInvoke(new System.Action(() => FilterLogs(ViewModel?.SearchText ?? string.Empty)),
                                   DispatcherPriority.Background);
        }

        private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;

            // 새 로그가 들어오면 '일치/전체' 표시도 같이 갱신합니다.
            Dispatcher.BeginInvoke(new System.Action(UpdateLogCounts), DispatcherPriority.Background);

            if (ViewModel?.IsAutoScrollEnabled == true)
            {
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    if (LogListView.Items.Count > 0)
                    {
                        var lastItem = LogListView.Items[LogListView.Items.Count - 1];
                        LogListView.ScrollIntoView(lastItem);
                    }
                }), DispatcherPriority.ApplicationIdle);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterLogs(ViewModel?.SearchText ?? string.Empty);
        }

        /// <summary>
        /// 검색어로 로그 목록을 걸러 내고, 검색 상자 오른쪽의 '일치/전체' 표시를 갱신합니다.
        /// </summary>
        private void FilterLogs(string searchText)
        {
            if (LogListView.ItemsSource == null)
            {
                if (ViewModel != null) ViewModel.LogCountText = "0";
                return;
            }

            var view = CollectionViewSource.GetDefaultView(LogListView.ItemsSource);

            if (string.IsNullOrWhiteSpace(searchText))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = item =>
                {
                    if (item is LogEntry entry)
                    {
                        // entry.Length는 '실제로 오간 길이'이고, Data는 표시 상한까지만 보관됩니다.
                        // 따라서 보유한 배열 길이를 기준으로 변환해야 범위를 넘지 않습니다.
                        string hexData = entry.Data != null ? Common.ByteConverter.ToHexString(entry.Data, entry.Data.Length) : "";
                        return entry.DisplayMessage.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                               hexData.Replace(" ", "").IndexOf(searchText.Replace(" ", ""), System.StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    return false;
                };
            }

            UpdateLogCounts();
        }

        /// <summary>
        /// 현재 필터에 걸린 개수와 전체 개수를 ViewModel에 알립니다. (예: "4/312")
        /// </summary>
        private void UpdateLogCounts()
        {
            var vm = ViewModel;
            if (vm == null) return;

            int total = vm.SelectedConnection?.Logs.Count ?? 0;

            if (LogListView.ItemsSource == null)
            {
                vm.LogCountText = "0";
                return;
            }

            var view = CollectionViewSource.GetDefaultView(LogListView.ItemsSource);

            // 필터가 없으면 굳이 세지 않고 전체 개수만 보여 줍니다.
            if (view.Filter == null)
            {
                vm.LogCountText = total.ToString();
                return;
            }

            int matched = view.Cast<object>().Count();
            vm.LogCountText = $"{matched}/{total}";
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            ViewModel?.UpdateCommandStates();
        }
    }
}
