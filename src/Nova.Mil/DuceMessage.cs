using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.Mil;

/// <summary>
/// Pack=1 layout matching <c>DUCE.MilMessage.Message</c> (Type@0, Reserved@4, payload@8).
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 32)]
public readonly struct DuceMessage : IEquatable<DuceMessage>
{
    [FieldOffset(0)]
    public readonly int Type;

    [FieldOffset(4)]
    public readonly int Reserved;

    [FieldOffset(8)]
    public readonly int PresentationResults;

    [FieldOffset(12)]
    public readonly int RefreshRate;

    [FieldOffset(16)]
    public readonly long PresentationTime;

    public DuceMessage(int type, int presentationResults = 0, int refreshRate = 0, long presentationTime = 0)
        : this()
    {
        Type = type;
        PresentationResults = presentationResults;
        RefreshRate = refreshRate;
        PresentationTime = presentationTime;
    }

    public bool Equals(DuceMessage other)
    {
        return Type == other.Type
            && Reserved == other.Reserved
            && PresentationResults == other.PresentationResults
            && RefreshRate == other.RefreshRate
            && PresentationTime == other.PresentationTime;
    }

    public override bool Equals(object? obj)
    {
        return obj is DuceMessage other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Reserved, PresentationResults, RefreshRate, PresentationTime);
    }

    public static bool operator ==(DuceMessage left, DuceMessage right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(DuceMessage left, DuceMessage right)
    {
        return !left.Equals(right);
    }
}
