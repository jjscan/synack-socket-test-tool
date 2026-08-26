using System.Collections.Generic;
using System.Text;

namespace SocketTestTool.Common
{
    /// <summary>
    /// 문자열에 포함된 보이지 않는 아스키(ASCII) 제어 문자를 "[STX]", "[CR]" 등 눈에 보이는 태그로 변환하는 정적 헬퍼 클래스입니다.
    /// '기호 보기(Show Symbols)' 기능의 핵심 로직을 담당합니다.
    /// </summary>
    public static class SymbolRenderer
    {
        #region Fields

        /// <summary>
        /// 시각적으로 표현할 제어 문자와, 그에 해당하는 태그 문자열을 매핑하는 사전(Dictionary)입니다.
        /// </summary>
        private static readonly Dictionary<char, string> _symbolMap = new Dictionary<char, string>
        {
            { '\u0000', "[NULL]" }, // Null
            { '\u0002', "[STX]" },  // Start of Text
            { '\u0003', "[ETX]" },  // End of Text
            { '\u0004', "[EOT]" },  // End of Transmission
            { '\u0005', "[ENQ]" },  // Enquiry
            { '\u0006', "[ACK]" },  // Acknowledge
            { '\u0015', "[NAK]" },  // Negative Acknowledge
            { '\r',     "[CR]" },   // Carriage Return
            { '\n',     "[LF]" }    // Line Feed
        };

        #endregion

        #region Public Static Methods

        /// <summary>
        /// 입력된 문자열을 한 글자씩 검사하여, 제어 문자를 시각적 태그로 변환합니다.
        /// </summary>
        /// <param name="text">변환할 원본 문자열입니다.</param>
        /// <returns>제어 문자가 태그로 변환된 새로운 문자열을 반환합니다.</returns>
        public static string Render(string text)
        {
            // 입력 값이 없으면 불필요한 처리를 건너뜁니다.
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (char c in text)
            {
                // 현재 문자가 _symbolMap에 정의된 제어 문자인지 확인합니다.
                if (_symbolMap.ContainsKey(c))
                {
                    // 제어 문자일 경우, 매핑된 태그(예: "[CR]")를 추가합니다.
                    sb.Append(_symbolMap[c]);
                }
                else
                {
                    // 일반 문자일 경우, 그대로 추가합니다.
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        #endregion
    }
}