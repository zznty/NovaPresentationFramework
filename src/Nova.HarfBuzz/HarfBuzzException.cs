using JetBrains.Annotations;

namespace Nova.HarfBuzz;

[PublicAPI]
public sealed class HarfBuzzException : Exception
{
    public HarfBuzzException()
    {
    }

    public HarfBuzzException(string message)
        : base(message)
    {
    }

    public HarfBuzzException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
