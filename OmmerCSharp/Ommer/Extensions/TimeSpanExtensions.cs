namespace Ommer.Extensions;

public static class TimeSpanExtensions
{
    public static string FormatHMS(this TimeSpan timeSpan)
    {
        return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
    }
}
