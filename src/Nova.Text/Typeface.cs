using JetBrains.Annotations;
using Nova.FontConfig;
using Nova.FreeType;

namespace Nova.Text;

/// <summary>Opened FreeType face plus a host-assigned <see cref="FaceId"/> used as a glyph-atlas key.</summary>
[PublicAPI]
public sealed class Typeface : IDisposable
{
    internal Typeface(uint faceId, FontFace face, FontMatch match)
    {
        FaceId = faceId;
        Face = face;
        Match = match;
    }

    /// <summary>
    /// Wraps a live FreeType face already opened by the DWrite host (token =
    /// <see cref="FontFace.NativeFaceHandle"/>). Does not take ownership of the face.
    /// </summary>
    public Typeface(uint faceId, FontFace face)
        : this(faceId, RequireFace(face), MatchFor(face))
    {
    }

    private static FontFace RequireFace(FontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        return face;
    }

    private static FontMatch MatchFor(FontFace face)
    {
        string family = face.FamilyName.Length == 0 ? "unknown" : face.FamilyName;
        return new FontMatch(family, "native", 0, 400, 0, 100);
    }

    public uint FaceId { get; }

    public FontFace Face { get; }

    public FontMatch Match { get; }

    public bool OwnsFace { get; init; } = true;

    public void Dispose()
    {
        if (OwnsFace)
        {
            Face.Dispose();
        }
    }
}
