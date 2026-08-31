using JetBrains.Annotations;

namespace Nova.SystemTheme;

/// <summary>
/// Live source for host metrics consumed by <see cref="HostTheme"/>. Implementations must
/// not throw: <see cref="HostTheme"/> keeps its hardcoded Classic defaults when a query fails.
/// </summary>
[PublicAPI]
public interface IHostMetrics
{
    /// <summary>Win32 <c>LOGPIXELSX</c>/<c>LOGPIXELSY</c> in DPI, at least 96.</summary>
    public int PixelsPerInch { get; }

    /// <summary>Win32 <c>GetDoubleClickTime</c> in milliseconds.</summary>
    public int DoubleClickTime { get; }

    /// <summary>Win32 <c>SystemParametersInfo(SPI_GETWORKAREA)</c>, in physical pixels.</summary>
    public void GetWorkArea(out int left, out int top, out int right, out int bottom);

    /// <summary>
    /// Win32 <c>GetSystemMetrics</c> via <see cref="SystemMetricIndex"/>. Indices this provider
    /// does not back return 0.
    /// </summary>
    public int GetSystemMetric(int index);

    /// <summary>
    /// Optional Win32 <c>COLORREF</c> override for <see cref="SystemColorIndex"/>. Return
    /// <c>null</c> for a slot to keep the hardcoded Classic color.
    /// </summary>
    public int? GetSysColor(int index)
    {
        return null;
    }

    /// <summary>
    /// Optional <c>NONCLIENTMETRICS</c> override (fonts and chrome metrics). Return <c>null</c>
    /// to keep the hardcoded Classic metrics.
    /// </summary>
    public NonClientMetrics? NonClient => null;
}
