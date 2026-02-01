using System.Globalization;
using System.Text.RegularExpressions;

namespace Deduplicator.Services;

public static class FilenameTimestampParser
{
    private static readonly List<TimestampPattern> Patterns = new()
    {
        // Android native camera: IMG_20230115_143052.jpg or IMG_20230115_143052123.jpg
        new TimestampPattern(
            @"(?:IMG|VID)_(\d{8})_(\d{6})(?:\d{3})?",
            groups => ParseCompactDateTime(groups[1].Value, groups[2].Value)
        ),

        // Generic compact format without prefix: 20230115_143052.jpg
        new TimestampPattern(
            @"^(\d{8})_(\d{6})(?:\d{3})?(?:[_-]\d+)?",
            groups => ParseCompactDateTime(groups[1].Value, groups[2].Value)
        ),

        // WhatsApp Android: IMG-20230115-WA0001.jpg
        new TimestampPattern(
            @"(?:IMG|VID)-(\d{8})-WA\d+",
            groups => ParseCompactDate(groups[1].Value)
        ),

        // WhatsApp iOS/Desktop: "WhatsApp Image 2023-01-15 at 14.30.52.jpeg"
        new TimestampPattern(
            @"WhatsApp (?:Image|Video) (\d{4})-(\d{2})-(\d{2}) at (\d{2})\.(\d{2})\.(\d{2})",
            groups => ParseDashedDateTime(
                groups[1].Value, groups[2].Value, groups[3].Value,
                groups[4].Value, groups[5].Value, groups[6].Value
            )
        ),

        // WhatsApp iOS Share Sheet: PHOTO-2023-01-15-14-30-52.jpg
        new TimestampPattern(
            @"(?:PHOTO|VIDEO)-(\d{4})-(\d{2})-(\d{2})-(\d{2})-(\d{2})-(\d{2})",
            groups => ParseDashedDateTime(
                groups[1].Value, groups[2].Value, groups[3].Value,
                groups[4].Value, groups[5].Value, groups[6].Value
            )
        ),

        // Screenshot Android: Screenshot_20230115-143052.png
        new TimestampPattern(
            @"Screenshot[_\s](\d{8})-(\d{6})",
            groups => ParseCompactDateTime(groups[1].Value, groups[2].Value)
        ),

        // Screenshot iOS: "Screenshot 2023-01-15 at 14.30.52.png"
        new TimestampPattern(
            @"Screenshot (\d{4})-(\d{2})-(\d{2}) at (\d{2})\.(\d{2})\.(\d{2})",
            groups => ParseDashedDateTime(
                groups[1].Value, groups[2].Value, groups[3].Value,
                groups[4].Value, groups[5].Value, groups[6].Value
            )
        ),

        // Signal: signal-2023-01-15-143052.jpg
        new TimestampPattern(
            @"signal-(\d{4})-(\d{2})-(\d{2})-(\d{2})(\d{2})(\d{2})",
            groups => ParseDashedDateTime(
                groups[1].Value, groups[2].Value, groups[3].Value,
                groups[4].Value, groups[5].Value, groups[6].Value
            )
        ),

        // Generic dashed format: YYYY-MM-DD_HH-MM-SS or YYYY-MM-DD HH.MM.SS
        new TimestampPattern(
            @"(\d{4})[_-](\d{2})[_-](\d{2})[_\s]+(\d{2})[._-](\d{2})[._-](\d{2})",
            groups => ParseDashedDateTime(
                groups[1].Value, groups[2].Value, groups[3].Value,
                groups[4].Value, groups[5].Value, groups[6].Value
            )
        ),

        // Date only formats (set time to 00:00:00)
        // YYYYMMDD or YYYY-MM-DD
        new TimestampPattern(
            @"(?:^|[_-])(\d{4})[-]?(\d{2})[-]?(\d{2})(?:[_-]|$)",
            groups => ParseCompactDate($"{groups[1].Value}{groups[2].Value}{groups[3].Value}")
        )
    };

    /// <summary>
    /// Attempts to extract a Unix timestamp from a filename.
    /// Returns null if no valid timestamp pattern is found.
    /// </summary>
    public static long? ParseTimestamp(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return null;

        // Remove extension for matching
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);

        foreach (var pattern in Patterns)
        {
            var match = Regex.Match(nameWithoutExtension, pattern.Pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                try
                {
                    var dateTime = pattern.Parser(match.Groups);
                    if (dateTime.HasValue)
                    {
                        return new DateTimeOffset(dateTime.Value, TimeSpan.Zero).ToUnixTimeSeconds();
                    }
                }
                catch
                {
                    // Invalid date/time, continue to next pattern
                }
            }
        }

        return null;
    }

    private static DateTime? ParseCompactDateTime(string datePart, string timePart)
    {
        // datePart: YYYYMMDD
        // timePart: HHMMSS
        if (datePart.Length != 8 || timePart.Length != 6)
            return null;

        var dateTimeString = $"{datePart}{timePart}";
        if (DateTime.TryParseExact(
            dateTimeString,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var result))
        {
            return DateTime.SpecifyKind(result, DateTimeKind.Utc);
        }

        return null;
    }

    private static DateTime? ParseCompactDate(string datePart)
    {
        // datePart: YYYYMMDD
        if (datePart.Length != 8)
            return null;

        if (DateTime.TryParseExact(
            datePart,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var result))
        {
            return DateTime.SpecifyKind(result, DateTimeKind.Utc);
        }

        return null;
    }

    private static DateTime? ParseDashedDateTime(
        string year, string month, string day,
        string hour, string minute, string second)
    {
        var dateTimeString = $"{year}-{month}-{day} {hour}:{minute}:{second}";
        if (DateTime.TryParseExact(
            dateTimeString,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var result))
        {
            return DateTime.SpecifyKind(result, DateTimeKind.Utc);
        }

        return null;
    }

    private class TimestampPattern
    {
        public string Pattern { get; }
        public Func<GroupCollection, DateTime?> Parser { get; }

        public TimestampPattern(string pattern, Func<GroupCollection, DateTime?> parser)
        {
            Pattern = pattern;
            Parser = parser;
        }
    }
}
