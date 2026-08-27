using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SocketTestTool.Models
{
    /// <summary>
    /// 단일 소켓 연결(서버 또는 클라이언트)의 모든 상태와 설정 정보를 담고 있는 데이터 모델 클래스입니다.
    /// INotifyPropertyChanged를 구현하여 속성 변경 시 UI가 자동으로 업데이트되도록 합니다.
    /// </summary>
    public class ConnectionModel : INotifyPropertyChanged
    {
        #region Fields (Private Backing Fields)

        private int _seq;
        private string? _status;
        private string? _address;
        private long _bytesSent;
        private long _bytesReceived;
        private bool _isPeriodicSending;
        private string _encodingName = "ASCII";

        #endregion

        #region Properties for UI Binding & Serialization

        /// <summary>
        /// UI 목록에 표시될 순번입니다.
        /// </summary>
        public int Seq
        {
            get => _seq;
            set { if (_seq != value) { _seq = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 연결 타입입니다. ("Server" 또는 "Client")
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// UI에 표시될 주소 문자열입니다. (예: "127.0.0.1:8080")
        /// </summary>
        public string? Address
        {
            get => _address;
            set { if (_address != value) { _address = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 현재 연결 상태입니다. (예: "Stopped", "Listening", "Connected")
        /// </summary>
        public string? Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 연결의 IP 주소입니다.
        /// </summary>
        public string? IpAddress { get; set; }

        /// <summary>
        /// 연결의 포트 번호입니다.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 서버의 기본 응답 패턴입니다. (예: "Echo", "SendOnce")
        /// </summary>
        public string? ResponsePattern { get; set; }

        /// <summary>
        /// 서버의 규칙 기반 응답 목록입니다.
        /// </summary>
        public List<ResponseRule> Rules { get; set; } = new List<ResponseRule>();

        /// <summary>
        /// 세션 로드 시 자동으로 연결을 시작할지 여부입니다.
        /// </summary>
        public bool AutoStart { get; set; } = true;

        /// <summary>
        /// '수신 후 응답' 패턴에서 사용할 응답 메시지입니다.
        /// </summary>
        public string? ReplyMessage { get; set; }

        /// <summary>
        /// '수신 후 응답' 패턴이 지속적으로 응답할지 여부입니다.
        /// </summary>
        public bool IsReplyEndless { get; set; }

        /// <summary>
        /// 수신 타임아웃 (기본값 300ms) : 설정된 시간동안 추가 데이터가 오는지 기다립니다.(Fragmentation 방지용)
        /// </summary>
        public int ReceiveTimeout { get; set; } = 300;

        /// <summary>
        /// 이 연결에서 사용할 인코딩의 이름입니다. (기본값 "ASCII", 그 외 "UTF-8" / "EUC-KR")
        /// </summary>
        public string EncodingName
        {
            get => _encodingName;
            set { if (_encodingName != value) { _encodingName = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 수신한 데이터를 다른 소켓 서버로 자동 전달할지 여부입니다.
        /// </summary>
        public bool IsForwardingEnabled { get; set; } = false;

        /// <summary>
        /// 수신 데이터를 자동 전달할 대상 서버의 IP 주소입니다.
        /// </summary>
        public string? ForwardIpAddress { get; set; }

        /// <summary>
        /// 수신 데이터를 자동 전달할 대상 서버의 포트 번호입니다.
        /// </summary>
        public int ForwardPort { get; set; }

        /// <summary>
        /// 실시간 로그 파일 저장 기능 활성화 여부입니다.
        /// </summary>
        public bool IsRealtimeLogEnabled { get; set; } = false;

        /// <summary>
        /// 실시간 로그를 저장할 파일의 전체 경로입니다.
        /// </summary>
        public string? LogFilePath { get; set; }

        #endregion

        #region Runtime Properties (Not Serialized)

        /// <summary>
        /// 현재 주기적 전송이 활성화되었는지 여부입니다. (실시간 상태)
        /// </summary>
        [JsonIgnore]
        public bool IsPeriodicSending
        {
            get => _isPeriodicSending;
            set { if (_isPeriodicSending != value) { _isPeriodicSending = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 실제 소켓 통신을 담당하는 Manager 객체입니다. (TcpServerManager 또는 TcpClientManager)
        /// </summary>
        [JsonIgnore]
        public object? Manager { get; set; }

        /// <summary>
        /// 수신 데이터를 다른 서버로 전달하는 ForwardingClient 객체입니다. (전달 기능이 꺼져 있으면 null)
        /// </summary>
        [JsonIgnore]
        public object? Forwarder { get; set; }

        /// <summary>
        /// 이 연결에 대한 로그 항목들을 담고 있는 컬렉션입니다.
        /// </summary>
        [JsonIgnore]
        public ObservableCollection<LogEntry> Logs { get; } = new ObservableCollection<LogEntry>();

        /// <summary>
        /// 총 전송된 바이트 수입니다.
        /// </summary>
        [JsonIgnore]
        public long BytesSent
        {
            get => _bytesSent;
            set { if (_bytesSent != value) { _bytesSent = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 총 수신된 바이트 수입니다.
        /// </summary>
        [JsonIgnore]
        public long BytesReceived
        {
            get => _bytesReceived;
            set { if (_bytesReceived != value) { _bytesReceived = value; OnPropertyChanged(); } }
        }

        private int _messageCount;
        /// <summary>
        /// 이 연결에서 오간 메시지(로그 항목)의 총 개수입니다. 로그 순환 버퍼와 무관하게 계속 누적됩니다.
        /// </summary>
        [JsonIgnore]
        public int MessageCount
        {
            get => _messageCount;
            set { if (_messageCount != value) { _messageCount = value; OnPropertyChanged(); } }
        }

        private double _messagesPerSecond;
        /// <summary>
        /// 최근 몇 초 동안의 초당 메시지 수입니다. (목업의 msg/s 지표)
        /// </summary>
        [JsonIgnore]
        public double MessagesPerSecond
        {
            get => _messagesPerSecond;
            set
            {
                if (Math.Abs(_messagesPerSecond - value) > 0.01)
                {
                    _messagesPerSecond = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _metaText;
        /// <summary>
        /// 연결 카드 두 번째 줄에 표시되는 요약입니다. (예: "Echo · ASCII · 주기전송 1000ms")
        /// </summary>
        [JsonIgnore]
        public string? MetaText
        {
            get => _metaText;
            set { if (_metaText != value) { _metaText = value; OnPropertyChanged(); } }
        }

        private string? _errorText;
        /// <summary>
        /// 연결 카드에 붉게 표시되는 오류 한 줄입니다. 오류가 없으면 비어 있습니다.
        /// </summary>
        [JsonIgnore]
        public string? ErrorText
        {
            get => _errorText;
            set { if (_errorText != value) { _errorText = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// LogService에서 이 연결을 식별하기 위한 고유 ID입니다.
        /// </summary>
        [JsonIgnore]
        public string Id { get; } = Guid.NewGuid().ToString();

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}