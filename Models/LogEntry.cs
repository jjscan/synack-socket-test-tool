using System;

namespace SocketTestTool.Models
{
    /// <summary>
    /// 로그의 방향(시스템, 전송, 수신)을 정의하는 열거형(Enum)입니다.
    /// </summary>
    public enum LogDirection { System, Sent, Received }

    /// <summary>
    /// 단일 로그 항목의 모든 정보를 담고 있는 데이터 모델 클래스입니다.
    /// </summary>
    public class LogEntry
    {
        #region Properties

        /// <summary>
        /// 로그가 생성된 시간입니다.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 로그의 방향입니다. (시스템 메시지, 보낸 데이터, 받은 데이터)
        /// </summary>
        public LogDirection Direction { get; set; }

        /// <summary>
        /// 로그의 부가적인 정보 메시지입니다. (예: 접속한 클라이언트 IP)
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 원본 데이터의 바이트 길이입니다.
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// 통신으로 주고받은 원본 바이트 배열 데이터입니다.
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// 원본 바이트 데이터를 UI에 선택된 인코딩으로 해석한 문자열입니다.
        /// </summary>
        public string DecodedData { get; set; }

        /// <summary>
        /// DecodedData에서 제어 문자를 [STX]와 같은 태그로 변환한, '기호 보기'용 문자열입니다.
        /// </summary>
        public string RenderedData { get; set; }

        #endregion

        #region Display Limit

        /// <summary>
        /// 화면과 로그 파일에 보여 줄 최대 바이트 수입니다.
        ///
        /// 원본 바이트(Data)는 항상 그대로 보관하지만, 표시용 문자열은 여기까지만 만듭니다.
        /// 상한이 없으면 1 MB 메시지 한 건이 디코딩 문자열 2 MB + 기호 문자열 2 MB + Hex 문자열 3 MB를
        /// 만들어 내고, 그 긴 문자열을 TextBlock이 줄바꿈 배치하느라 UI가 수 초씩 멈춥니다.
        /// </summary>
        public const int DisplayByteLimit = 4096;

        /// <summary>표시용으로 잘린 메시지인지 여부입니다.</summary>
        public bool IsTruncatedForDisplay => Length > DisplayByteLimit;

        /// <summary>잘렸을 때 뒤에 붙는 안내 문구입니다.</summary>
        private string TruncationNote =>
            IsTruncatedForDisplay ? $" … (총 {Length:N0} 바이트 중 {DisplayByteLimit:N0} 바이트만 표시)" : string.Empty;

        /// <summary>표시용으로 실제로 사용할 바이트 수입니다.</summary>
        public int DisplayLength => Math.Min(Length, DisplayByteLimit);

        #endregion

        #region Column Properties for the Fluent Log Table

        /// <summary>
        /// 로그 행 왼쪽에 표시되는 시각입니다. (예: "10:42:07.556")
        /// </summary>
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");

        /// <summary>
        /// 로그 행 오른쪽에 표시되는 길이입니다. 데이터가 없는 시스템 메시지는 "—"로 표시합니다.
        /// </summary>
        public string ByteCountText => Length > 0 ? $"{Length} B" : "—";

        /// <summary>
        /// 원본 바이트를 16진수 문자열로 표현한 값입니다. (Hex 보기용)
        /// </summary>
        // Data는 표시 상한까지만 보관될 수 있으므로(MainViewModel.HandleLogEntry에서 트리밍),
        // Length(실제 오간 길이)가 아니라 '실제 보유한 배열 길이'로 변환해야 범위를 넘지 않습니다.
        public string HexText => Data != null && Data.Length > 0
            ? Common.ByteConverter.ToHexString(Data, Math.Min(Data.Length, DisplayLength)) + TruncationNote
            : string.Empty;

        /// <summary>
        /// 실제 통신 데이터를 담고 있는 행인지 여부입니다. (시스템 메시지는 false)
        /// </summary>
        public bool HasData => Data != null && Data.Length > 0;

        /// <summary>
        /// 'Text' 보기에서 본문 칸에 들어갈 문자열입니다.
        /// 데이터가 있으면 해석된 문자열을, 없으면 시스템 메시지를 보여 줍니다.
        /// </summary>
        public string PayloadText => string.IsNullOrEmpty(DecodedData) ? Message : DecodedData + TruncationNote;

        /// <summary>
        /// '기호' 보기에서 본문 칸에 들어갈 문자열입니다. 제어 문자가 [STX] 형태로 보입니다.
        /// </summary>
        public string RenderedPayloadText => string.IsNullOrEmpty(RenderedData) ? Message : RenderedData + TruncationNote;

        /// <summary>
        /// 데이터 행에서, 본문과 별개로 덧붙는 부가 정보입니다.
        /// (예: 서버가 어느 클라이언트에게서 받았는지)
        /// </summary>
        public string SideNote => string.IsNullOrEmpty(DecodedData) || string.IsNullOrEmpty(Message)
            ? string.Empty
            : Message;

        #endregion

        #region Display Properties for UI Binding

        /// <summary>
        /// UI의 '기본 보기' 로그 목록에 표시될 최종 포맷의 문자열입니다.
        /// </summary>
        public string DisplayMessage
        {
            get
            {
                // DecodedData 속성에 값이 있으면 공백과 함께 추가, 없으면 빈 문자열
                string dataAsString = string.IsNullOrEmpty(DecodedData) ? "" : " " + DecodedData + TruncationNote;
                // Length가 0보다 클 때만 바이트 정보 표시
                string lengthInfo = Length > 0 ? $" ({Length} bytes)" : "";
                // Message 속성에 내용이 있을 때만 콜론과 함께 표시
                string messagePart = string.IsNullOrEmpty(Message) ? "" : $": {Message}";

                return $"[{Timestamp:HH:mm:ss.fff}] [{Direction}]{messagePart}{dataAsString}{lengthInfo}";
            }
        }

        /// <summary>
        /// UI의 '기호 보기' 로그 목록에 표시될 최종 포맷의 문자열입니다.
        /// </summary>
        public string RenderedDisplayMessage
        {
            get
            {
                string dataAsString = string.IsNullOrEmpty(RenderedData) ? "" : " " + RenderedData + TruncationNote;
                string lengthInfo = Length > 0 ? $" ({Length} bytes)" : "";
                string messagePart = string.IsNullOrEmpty(Message) ? "" : $": {Message}";

                return $"[{Timestamp:HH:mm:ss.fff}] [{Direction}]{messagePart}{dataAsString}{lengthInfo}";
            }
        }

        #endregion
    }
}