using System;
using System.Windows.Input;

namespace SocketTestTool.Common
{
    /// <summary>
    /// MVVM 패턴에서 View(UI)의 컨트롤과 ViewModel의 로직을 연결하기 위한 ICommand 인터페이스의 표준 구현체입니다.
    /// 이 클래스는 View의 이벤트(예: Button.Click)를 ViewModel의 메서드(Action)로 전달(Relay)하는 역할을 합니다.
    /// </summary>
    public class RelayCommand : ICommand
    {
        #region Fields

        // Command가 실행할 실제 로직(메서드)을 저장하는 델리게이트입니다.
        private readonly Action<object> _execute;
        // Command의 실행 가능 여부를 판단하는 로직(메서드)을 저장하는 델리게이트입니다.
        private readonly Predicate<object> _canExecute;

        #endregion

        #region Constructors

        /// <summary>
        /// 실행 가능 여부 판단 없이 항상 실행 가능한 Command를 생성합니다.
        /// </summary>
        /// <param name="execute">실행할 Action<object> 델리게이트입니다.</param>
        public RelayCommand(Action<object> execute)
            : this(execute, null)
        {
        }

        /// <summary>
        /// 실행 로직과 실행 가능 여부 판단 로직을 모두 갖는 Command를 생성합니다.
        /// </summary>
        /// <param name="execute">실행할 Action<object> 델리게이트입니다.</param>
        /// <param name="canExecute">실행 가능 여부를 판단할 Predicate<object> 델리게이트입니다.</param>
        public RelayCommand(Action<object> execute, Predicate<object> canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        #endregion

        #region ICommand Implementation

        /// <summary>
        /// Command의 실행 가능 여부를 반환합니다. 이 결과는 UI 컨트롤의 IsEnabled 속성에 반영됩니다.
        /// </summary>
        public bool CanExecute(object parameter)
        {
            // _canExecute 델리게이트가 없으면 무조건 true, 있으면 해당 델리게이트의 결과 반환
            return _canExecute == null || _canExecute(parameter);
        }

        /// <summary>
        /// Command의 실행 가능 상태가 변경되었음을 UI에 알리는 이벤트입니다.
        /// WPF의 CommandManager.RequerySuggested 이벤트를 통해 UI 상태 변경 시 자동으로 CanExecute가 다시 호출되도록 합니다.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Command의 실제 로직을 실행합니다.
        /// </summary>
        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        /// <summary>
        /// ViewModel에서 코드상으로 Command의 실행 가능 상태가 변경되었음을 강제로 알릴 때 사용하는 메서드입니다.
        /// (예: MainViewModel의 UpdateCommandStates() 내부에서 호출됨)
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        #endregion
    }
}