using SocketTestTool.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SocketTestTool.Common
{
    /// <summary>
    /// 변환기들이 공용으로 쓰는 브러시 모음입니다.
    /// Themes/Fluent.xaml의 토큰과 같은 값을 코드 쪽에서도 쓰기 위해 한 곳에 모아 두었습니다.
    /// </summary>
    internal static class UiBrushes
    {
        public static readonly SolidColorBrush Accent = Freeze("#FF0F6CBD");
        public static readonly SolidColorBrush AccentBadge = Freeze("#1F0F6CBD");
        public static readonly SolidColorBrush AccentRowTint = Freeze("#0B0F6CBD");
        public static readonly SolidColorBrush Success = Freeze("#FF0F7B0F");
        public static readonly SolidColorBrush SuccessBadge = Freeze("#1F0F7B0F");
        public static readonly SolidColorBrush SuccessRowTint = Freeze("#0B0F7B0F");
        public static readonly SolidColorBrush Danger = Freeze("#FFC42B1C");
        public static readonly SolidColorBrush DangerBadge = Freeze("#1FC42B1C");
        public static readonly SolidColorBrush DangerBorder = Freeze("#59C42B1C");
        public static readonly SolidColorBrush Warning = Freeze("#FF7A4D00");
        public static readonly SolidColorBrush WarningBadge = Freeze("#1F9D5D00");
        public static readonly SolidColorBrush TextPrimary = Freeze("#FF1B1B1B");
        public static readonly SolidColorBrush TextSecondary = Freeze("#FF5D5D5D");
        public static readonly SolidColorBrush TextMuted = Freeze("#FF8A8A8A");
        public static readonly SolidColorBrush StoppedDot = Freeze("#FF9A9A9A");
        public static readonly SolidColorBrush ControlFill = Freeze("#FFF0F0F0");
        public static readonly SolidColorBrush CardBorder = Freeze("#0F000000");

        private static SolidColorBrush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze(); // 여러 스레드/요소에서 공유되므로 고정합니다.
            return brush;
        }
    }

    /// <summary>
    /// 로그 방향(System/Sent/Received)을 목업의 배지 문자열로 바꿉니다.
    /// </summary>
    public class LogDirectionToBadgeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is LogDirection direction)
            {
                switch (direction)
                {
                    case LogDirection.Sent: return "▲ SENT";
                    case LogDirection.Received: return "▼ RECV";
                    default: return "SYS";
                }
            }
            return "SYS";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 로그 방향을 색으로 바꿉니다.
    /// ConverterParameter로 어느 색을 원하는지 지정합니다: "Foreground" | "Badge" | "RowTint"
    /// </summary>
    public class LogDirectionToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var direction = value is LogDirection d ? d : LogDirection.System;
            string role = parameter as string ?? "Foreground";

            switch (role)
            {
                case "Badge":
                    if (direction == LogDirection.Sent) return UiBrushes.AccentBadge;
                    if (direction == LogDirection.Received) return UiBrushes.SuccessBadge;
                    return UiBrushes.ControlFill;

                case "RowTint":
                    if (direction == LogDirection.Sent) return UiBrushes.AccentRowTint;
                    if (direction == LogDirection.Received) return UiBrushes.SuccessRowTint;
                    return Brushes.Transparent;

                default:
                    if (direction == LogDirection.Sent) return UiBrushes.Accent;
                    if (direction == LogDirection.Received) return UiBrushes.Success;
                    return UiBrushes.TextSecondary;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 연결 타입("Server"/"Client")을 SRV/CLI 배지의 문자열과 색으로 바꿉니다.
    /// ConverterParameter: "Text" | "Foreground" | "Background"
    /// </summary>
    public class ConnectionTypeToBadgeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isServer = (value as string) == "Server";
            switch (parameter as string)
            {
                case "Foreground": return isServer ? UiBrushes.Accent : UiBrushes.Warning;
                case "Background": return isServer ? UiBrushes.AccentBadge : UiBrushes.WarningBadge;
                default: return isServer ? "SRV" : "CLI";
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 연결 상태 문자열을 상태 필(pill)의 표시 문자열과 색으로 바꿉니다.
    /// ConverterParameter: "Text" | "Foreground" | "Dot"
    /// </summary>
    public class StatusToPillConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string status = value as string ?? "Stopped";
            bool isActive = status == "Listening" || status == "Connected";
            bool isError = status == "Error";

            switch (parameter as string)
            {
                case "Foreground":
                case "Dot":
                    if (isActive) return UiBrushes.Success;
                    if (isError) return UiBrushes.Danger;
                    return parameter as string == "Dot" ? UiBrushes.StoppedDot : (object)UiBrushes.TextSecondary;

                default:
                    return status.ToUpperInvariant();
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 바이트 수를 사람이 읽기 좋은 단위로 바꿉니다. (예: 1234 -> "1.2 KB")
    /// </summary>
    public class ByteCountToHumanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double bytes = value is long l ? l : (value is int i ? i : 0);

            if (bytes < 1024) return $"{bytes:0} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024:0.0} KB";
            return $"{bytes / (1024 * 1024):0.0} MB";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 비어 있지 않은 문자열이면 Visible, 비어 있으면 Collapsed를 반환합니다.
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 항목 수가 0일 때 Visible을 반환합니다. (빈 상태 화면 표시용)
    /// ConverterParameter에 "Invert"를 주면 반대로 동작합니다.
    /// </summary>
    public class EmptyCountToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            int count = value is int i ? i : 0;
            bool isEmpty = count == 0;
            if ((parameter as string) == "Invert") isEmpty = !isEmpty;
            return isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// bool 값을 뒤집어 Visibility로 바꿉니다. (false일 때 Visible)
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 연결이 멈춰 있으면 목업처럼 카드를 살짝 흐리게 만듭니다.
    /// </summary>
    public class StatusToOpacityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string status = value as string ?? "Stopped";
            bool isActive = status == "Listening" || status == "Connected" || status == "Error";
            return isActive ? 1.0 : 0.72;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 전체 경로에서 파일 이름만 뽑아냅니다. (최근 세션 목록 표시용)
    /// </summary>
    public class PathToFileNameConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string? path = value as string;
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            try { return System.IO.Path.GetFileName(path); }
            catch (ArgumentException) { return path; }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 알림 배너의 심각도를 색으로 바꿉니다.
    /// ConverterParameter: "Foreground" | "Background" | "Border"
    /// </summary>
    public class BannerSeverityToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var severity = value is BannerSeverity s ? s : BannerSeverity.Error;
            switch (parameter as string)
            {
                case "Background":
                    if (severity == BannerSeverity.Success) return UiBrushes.SuccessBadge;
                    if (severity == BannerSeverity.Warning) return UiBrushes.WarningBadge;
                    return UiBrushes.DangerBadge;

                case "Border":
                    if (severity == BannerSeverity.Success) return UiBrushes.Success;
                    if (severity == BannerSeverity.Warning) return UiBrushes.Warning;
                    return UiBrushes.DangerBorder;

                default:
                    if (severity == BannerSeverity.Success) return UiBrushes.Success;
                    if (severity == BannerSeverity.Warning) return UiBrushes.Warning;
                    return UiBrushes.Danger;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
