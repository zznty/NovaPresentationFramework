using JetBrains.Annotations;

namespace Nova.Imaging;

/// <summary>
/// Decoder metadata for the WIC <c>IWICBitmapCodecInfo</c> shim: the container-format GUID
/// (mapped by the nest from <see cref="WicContainerFormat"/>) and the codec's MIME-type list.
/// Created per <c>GetDecoderInfo</c> call and held in the <see cref="WicHandleTable"/>.
/// </summary>
[PublicAPI]
public sealed class ManagedWicDecoderInfo
{
    internal ManagedWicDecoderInfo(WicContainerFormat containerFormat, string mimeTypes)
    {
        ContainerFormat = containerFormat;
        MimeTypes = mimeTypes;
    }

    /// <summary>The decoded image's container format.</summary>
    public WicContainerFormat ContainerFormat { get; }

    /// <summary>Comma-separated MIME types, mirroring WIC's <c>GetMimeTypes</c>.</summary>
    public string MimeTypes { get; }
}
