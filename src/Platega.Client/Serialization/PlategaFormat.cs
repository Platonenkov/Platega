using System.Globalization;

namespace Platega.Serialization;

internal static class PlategaFormat
{
    /// <summary>Formats a timestamp the way the API examples do: UTC with milliseconds, e.g. <c>2026-05-01T00:00:00.000Z</c>.</summary>
    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string FormatId(Guid value) => value.ToString("D");
}
