using JetBrains.Annotations;

namespace Nova.MilCmd;

[PublicAPI]
public sealed class MilParseException : Exception
{
    public MilParseException()
    {
    }

    public MilParseException(string message)
        : base(message)
    {
    }

    public MilParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public MilParseException(string message, int offset)
        : base($"{message} at offset {offset}.")
    {
        Offset = offset;
    }

    public int Offset { get; }
}
