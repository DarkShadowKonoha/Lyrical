using System.Text.RegularExpressions;
using System.Text;

namespace Lyrical;

public sealed record TrackInfo(string Title, string Artist, string Album, long DurationMs, string? ArtworkDataUrl = null)
{
    public string Key => $"{Title}|{Artist}|{Album}";
}

public static partial class TrackNormalizer
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // Windows-1252's printable replacements for bytes 0x80-0x9f.  Metadata
    // providers occasionally decode UTF-8 with this code page before exposing
    // it through GSMTC (for example, "åŒ—æžçŒ´å­" instead of "北极猴子").
    private static readonly IReadOnlyDictionary<char, byte> Windows1252Bytes = new Dictionary<char, byte>
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85,
        ['†'] = 0x86, ['‡'] = 0x87, ['ˆ'] = 0x88, ['‰'] = 0x89, ['Š'] = 0x8A,
        ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92,
        ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97,
        ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C,
        ['ž'] = 0x9E, ['Ÿ'] = 0x9F
    };

    // Mirrors normalizeTitle() in web/engine.js: "Song - 2011 Remaster" -> "Song".
    [GeneratedRegex(@"\s*[-–]\s*(\d{4}\s*)?(remaster(ed)?|live|mono|stereo|single version|radio edit|deluxe|bonus track)[^()\[\]]*$", RegexOptions.IgnoreCase)]
    private static partial Regex Suffix();

    [GeneratedRegex(@"\s*[(\[]\s*(\d{4}\s*)?(remaster(ed)?|live|mono|stereo|deluxe|bonus track|feat\.?[^)\]]*|with [^)\]]*)\s*[)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Bracketed();

    // Some local-file and third-party tags pack an English title, artist, and
    // translated CJK alias into one field: "IWannaBeYours-ArcticMonkeys(北极猴子)".
    [GeneratedRegex(@"\s*[（(][^()（）]*[\u3400-\u9fff][^()（）]*[)）]\s*$")]
    private static partial Regex CjkAliasSuffix();

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CamelWordBoundary();

    public static string Title(string title)
    {
        var s = RepairMojibake(title);
        s = Suffix().Replace(s, "");
        s = Bracketed().Replace(s, "");
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    /// <summary>
    /// Cleans media metadata without changing normal multilingual titles. The
    /// packed-title rule only activates when a trailing CJK alias is present
    /// and the player did not provide a separate artist.
    /// </summary>
    public static TrackInfo CleanMetadata(TrackInfo track)
    {
        var title = RepairMojibake(track.Title);
        var artist = RepairMojibake(track.Artist);
        var album = RepairMojibake(track.Album);
        var withoutAlias = CjkAliasSuffix().Replace(title, "").Trim();
        var aliasWasRemoved = withoutAlias.Length != title.Length;

        if (aliasWasRemoved && string.IsNullOrWhiteSpace(artist))
        {
            var separator = withoutAlias.LastIndexOf('-');
            if (separator > 0 && separator < withoutAlias.Length - 1)
            {
                title = SpaceCamelWords(withoutAlias[..separator]);
                artist = SpaceCamelWords(withoutAlias[(separator + 1)..]);
            }
            else
            {
                title = withoutAlias;
            }
        }
        else
        {
            title = withoutAlias;
        }

        return track with { Title = title, Artist = artist, Album = album };
    }

    /// <summary>
    /// Repairs a UTF-8 byte sequence that was mistakenly decoded as Windows-1252.
    /// The check is deliberately narrow: valid UTF-8 is required and the original
    /// text must contain several characteristic mojibake markers.
    /// </summary>
    public static string RepairMojibake(string? value)
    {
        if (string.IsNullOrEmpty(value) || MojibakeMarkerCount(value) < 2)
            return value ?? string.Empty;

        var bytes = new byte[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character <= 0xFF)
            {
                bytes[i] = (byte)character;
            }
            else if (Windows1252Bytes.TryGetValue(character, out var b))
            {
                bytes[i] = b;
            }
            else
            {
                return value;
            }
        }

        try
        {
            var repaired = StrictUtf8.GetString(bytes);
            return MojibakeMarkerCount(repaired) < MojibakeMarkerCount(value) ? repaired : value;
        }
        catch (DecoderFallbackException)
        {
            return value;
        }
    }

    private static int MojibakeMarkerCount(string value) => value.Count(c =>
        c is 'Ã' or 'Â' or 'â' or 'å' or 'æ' or 'ç' or 'ð' or 'œ' or 'ž' ||
        (c >= '\u0080' && c <= '\u009F'));

    private static string SpaceCamelWords(string value) =>
        CamelWordBoundary().Replace(value.Trim(), " ");
}
