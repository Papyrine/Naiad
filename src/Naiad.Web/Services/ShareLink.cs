using System.IO.Compression;

namespace Naiad.Web.Services;

/// <summary>
/// Carries the diagram source in the URL fragment (<c>#code=…</c>, the UTF-8 bytes deflated and
/// base64url-encoded) so a link reopens it. The fragment never reaches the server, so only the browser's
/// URL limit applies; <see cref="MaxLength"/> keeps links short enough to paste anywhere.
/// </summary>
public static class ShareLink
{
    /// <summary>The longest fragment written. Larger sources get no link.</summary>
    public const int MaxLength = 8000;

    // Past this the source cannot fit MaxLength however well it compresses, so it is not worth deflating.
    const int maxSourceLength = 256 * 1024;

    /// <summary>The fragment (without <c>#</c>) for <paramref name="source"/>, or null when it is empty or too large.</summary>
    public static string? Encode(string source)
    {
        if (source.Length == 0 ||
            source.Length > maxSourceLength)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize))
        {
            deflate.Write(Encoding.UTF8.GetBytes(source));
        }

        var data = Convert.ToBase64String(buffer.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var fragment = $"code={data}";
        if (fragment.Length > MaxLength)
        {
            return null;
        }

        return fragment;
    }

    /// <summary>Reads a fragment (with or without <c>#</c>) back into the diagram source.</summary>
    public static bool TryDecode(string? fragment, out string source)
    {
        source = "";
        if (string.IsNullOrEmpty(fragment))
        {
            return false;
        }

        string? data = null;
        foreach (var pair in fragment.TrimStart('#').Split('&'))
        {
            var split = pair.IndexOf('=');
            if (split >= 0 &&
                pair[..split] == "code")
            {
                data = pair[(split + 1)..];
            }
        }

        if (string.IsNullOrEmpty(data))
        {
            return false;
        }

        try
        {
            var base64 = data.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            using var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.UTF8);
            source = reader.ReadToEnd();
            // A truncated stream can decode to nothing without throwing; an empty diagram is no link.
            return source.Length > 0;
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException)
        {
            return false;
        }
    }
}
