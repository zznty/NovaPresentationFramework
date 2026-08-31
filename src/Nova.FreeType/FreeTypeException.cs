using JetBrains.Annotations;

namespace Nova.FreeType;

[PublicAPI]
public sealed class FreeTypeException : Exception
{
    public FreeTypeException()
    {
    }

    public FreeTypeException(string message)
        : base(message)
    {
    }

    public FreeTypeException(string message, int error)
        : base($"{message} (FreeType error {error}).")
    {
        Error = error;
    }

    public FreeTypeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int Error { get; }
}
