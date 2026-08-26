using System.Text;

namespace SocketTestTool.Common
{
    /// <summary>
    /// 바이트(byte) 데이터 처리를 위한 정적 헬퍼 클래스입니다.
    /// </summary>
    public static class ByteConverter
    {
        #region Public Static Methods

        /// <summary>
        /// 바이트 배열을 공백으로 구분된 16진수(Hex) 문자열로 변환합니다.
        /// </summary>
        /// <param name="bytes">변환할 원본 바이트 배열입니다.</param>
        /// <param name="length">배열에서 변환할 길이입니다.</param>
        /// <returns>변환된 16진수 문자열을 반환합니다. (예: "0A 1B 2C")</returns>
        public static string ToHexString(byte[] bytes, int length)
        {
            // 입력 값이 유효하지 않으면 즉시 빈 문자열 반환
            if (bytes == null || length == 0)
            {
                return string.Empty;
            }

            // StringBuilder의 초기 용량을 미리 할당하여 성능 최적화
            // (예: 3바이트 -> "0A 1B 2C" -> 8글자, length * 3 - 1)
            var hex = new StringBuilder(length * 3);

            for (int i = 0; i < length; i++)
            {
                // 구분용 공백은 두 번째 바이트부터 '앞에' 붙입니다.
                // 뒤에 붙이면 문자열 끝에 불필요한 공백이 남습니다.
                if (i > 0) hex.Append(' ');
                hex.AppendFormat("{0:X2}", bytes[i]);
            }
            return hex.ToString();
        }

        #endregion
    }
}