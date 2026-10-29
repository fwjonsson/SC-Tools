using System.Text;

namespace SCTools.Tracking;

/// <summary>
/// data.json as it was made last: the text, the same as bytes (sent as they are, request after request), and an ETag that names this making of it. The
/// page sends the ETag back with its next poll (If-None-Match); the same one means nothing changed, and the server answers 304 with no body.
/// </summary>
public sealed class LiveSnapshot
{
    public LiveSnapshot(string json, string etag)
    {
        Json = json;
        Bytes = Encoding.UTF8.GetBytes(json);
        ETag = etag;
    }

    public string Json { get; }
    public byte[] Bytes { get; }
    public string ETag { get; }

    /// <summary>Before the first refresh: an empty page.</summary>
    public static readonly LiveSnapshot Empty = new("{}", "\"none\"");
}
