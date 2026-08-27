namespace SocketTestTool.Models
{
    /// <summary>
    /// 서버의 '규칙 기반 응답(Rule-based Response)' 기능에서 사용되는 단일 규칙을 정의하는 데이터 모델 클래스입니다.
    /// </summary>
    public class ResponseRule
    {
        #region Properties

        /// <summary>
        /// 클라이언트로부터 수신한 데이터에 포함되어 있는지 검사할 문자열입니다.
        /// </summary>
        public string? ReceiveData { get; set; }

        /// <summary>
        /// ReceiveData가 포함되어 있을 경우, 서버가 클라이언트에게 응답으로 보낼 문자열입니다.
        /// </summary>
        public string? SendData { get; set; }

        #endregion
    }
}