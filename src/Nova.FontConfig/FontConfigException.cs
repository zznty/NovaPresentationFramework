using JetBrains.Annotations;

namespace Nova.FontConfig;

[PublicAPI]
public sealed class FontConfigException : Exception
{
    public FontConfigException()
    {
    }

    public FontConfigException(string message)
        : base(message)
    {
    }

    public FontConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
