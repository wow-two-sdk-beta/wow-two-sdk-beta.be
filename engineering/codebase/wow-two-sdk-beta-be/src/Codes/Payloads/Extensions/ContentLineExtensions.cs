using System.Text;

namespace WoW.Two.Sdk.Beta.Be.Codes.Payloads.Extensions;

/// <summary>Extends payload formatting with escaped and folded UTF-8 content lines for contact and calendar documents.</summary>
internal static class ContentLineExtensions
{
    internal static string Escape(string? value) => (value?.Trim() ?? string.Empty)
        .Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal).ReplaceLineEndings("\\n");

    internal static string Join(IEnumerable<string> lines) => string.Join("\r\n", lines.Select(Fold));

    private static string Fold(string line)
    {
        var result = new StringBuilder();
        Span<char> utf16 = stackalloc char[2];
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 75) { result.Append("\r\n "); bytes = 1; }
            var written = rune.EncodeToUtf16(utf16);
            result.Append(utf16[..written]);
            bytes += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
}
