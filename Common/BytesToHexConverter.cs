using System;
using System.Globalization;
using System.Windows.Data;

namespace SocketTestTool.Common
{
    /// <summary>
    /// WPF XAML에서 데이터 바인딩 시 byte[]를 16진수(Hex) 문자열로 변환하는 ValueConverter입니다.
    /// </summary>
    public class BytesToHexConverter : IValueConverter
    {
        #region IValueConverter Implementation

        /// <summary>
        /// 소스(byte[])에서 타겟(string)으로 데이터 형식을 변환합니다.
        /// </summary>
        /// <param name="value">변환할 원본 byte[] 데이터입니다.</param>
        /// <param name="targetType">타겟 타입입니다 (string).</param>
        /// <param name="parameter">컨버터 파라미터입니다 (사용 안 함).</param>
        /// <param name="culture">문화권 정보입니다 (사용 안 함).</param>
        /// <returns>16진수 문자열로 변환된 결과를 반환합니다.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 입력된 값이 byte[] 타입인지 확인합니다.
            if (value is byte[] bytes)
            {
                // 이전에 만든 ByteConverter 헬퍼 클래스를 재사용하여 변환 로직을 처리합니다.
                return ByteConverter.ToHexString(bytes, bytes.Length);
            }

            // 변환할 수 없는 타입이면 빈 문자열을 반환합니다.
            return string.Empty;
        }

        /// <summary>
        /// 타겟(string)에서 소스(byte[])로 데이터 형식을 변환합니다. (One-Way 바인딩이므로 구현 불필요)
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 이 프로그램에서는 UI에서 Hex 문자열을 직접 수정하여 원래 byte[]로 되돌릴 필요가 없으므로
            // ConvertBack 기능은 구현하지 않습니다.
            throw new NotImplementedException();
        }

        #endregion
    }
}