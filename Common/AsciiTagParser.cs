using System;
using System.Collections.Generic;

namespace SocketTestTool.Common
{
    /// <summary>
    /// UI 텍스트에 포함된 제어 문자 태그(예: [STX])를 실제 아스키 제어 문자로 변환하는 정적 헬퍼 클래스입니다.
    /// </summary>
    public static class AsciiTagParser
    {
        #region Fields

        /// <summary>
        /// 변환할 제어 문자 태그와 실제 제어 문자를 매핑하는 사전(Dictionary)입니다.
        /// Key: UI에 표시될 태그 (예: "[STX]")
        /// Value: C#에서 사용하는 실제 제어 문자 (예: "\u0002")
        /// </summary>
        private static readonly Dictionary<string, string> _tagMap = new Dictionary<string, string>
        {
            { "[STX]", "\u0002" }, // Start of Text
            { "[ETX]", "\u0003" }, // End of Text
            { "[EOT]", "\u0004" }, // End of Transmission
            { "[ENQ]", "\u0005" }, // Enquiry
            { "[ACK]", "\u0006" }, // Acknowledge
            { "[NAK]", "\u0015" }, // Negative Acknowledge
            { "[CR]",  "\r"     }, // Carriage Return
            { "[LF]",  "\n"     }, // Line Feed
            { "[NULL]", "\u0000"}  // Null
        };

        #endregion

        #region Public Methods

        /// <summary>
        /// 입력된 문자열에서 제어 문자 태그를 찾아 실제 제어 문자로 변환합니다.
        /// </summary>
        /// <param name="input">변환할 원본 문자열입니다. (예: "[STX]DATA[ETX]")</param>
        /// <returns>태그가 실제 제어 문자로 변환된 문자열을 반환합니다.</returns>
        public static string Parse(string input)
        {
            // 입력 값이 null이거나 비어있으면, 불필요한 처리를 방지하기 위해 즉시 빈 문자열 반환
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string output = input;
            foreach (var pair in _tagMap)
            {
                // 대소문자를 구분하지 않고 모든 태그를 변환합니다. (예: [stx]와 [STX] 모두 처리)
                // StringComparison.OrdinalIgnoreCase는 문화권에 상관없이 일관된 비교를 보장하여 더 안정적입니다.
                output = output.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
            }
            return output;
        }

        #endregion
    }
}