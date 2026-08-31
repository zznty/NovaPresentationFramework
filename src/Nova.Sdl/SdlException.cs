using JetBrains.Annotations;

namespace Nova.Sdl;

[PublicAPI]
public sealed class SdlException : Exception
{
    public SdlException()
        : base("SDL operation failed.")
    {
    }

    public SdlException(string message)
        : base(message)
    {
    }

    public SdlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
