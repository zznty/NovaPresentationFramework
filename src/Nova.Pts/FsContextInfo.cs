using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.Pts;

/// <summary>
/// The client callback table Nova.Pts drives. Field-for-field equivalent of the subset of
/// <c>PTS.FSCONTEXTINFO.fscbk</c> (cbkgen + cbktxt) that the bottomless single-column text path
/// needs; the WPF nest wrapper builds this from the PtsHost-provided <c>FSCONTEXTINFO</c>.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct FsContextInfo : IEquatable<FsContextInfo>
{
    public uint Version;
    public uint Fsffi;
    public int DrMinColumnBalancingStep;
    public int CInstalledObjects;
    public IntPtr PInstalledObjects;
    public IntPtr PfsClient;
    public IntPtr PtsPenaltyModule;

    // cbkgen
    public PtsDelegates.GetPageDimensions GetPageDimensions;
    public PtsDelegates.GetNextSection GetNextSection;
    public PtsDelegates.GetSectionProperties GetSectionProperties;
    public PtsDelegates.GetJustificationProperties GetJustificationProperties;
    public PtsDelegates.GetMainTextSegment GetMainTextSegment;
    public PtsDelegates.GetHeaderSegment GetHeaderSegment;
    public PtsDelegates.GetFooterSegment GetFooterSegment;
    public PtsDelegates.GetSectionColumnInfo GetSectionColumnInfo;
    public PtsDelegates.GetSegmentDefinedColumnSpanAreaInfo GetSegmentDefinedColumnSpanAreaInfo;
    public PtsDelegates.GetHeightDefinedColumnSpanAreaInfo GetHeightDefinedColumnSpanAreaInfo;
    public PtsDelegates.GetFirstPara GetFirstPara;
    public PtsDelegates.GetNextPara GetNextPara;
    public PtsDelegates.UpdGetSegmentChange UpdGetSegmentChange;
    public PtsDelegates.GetParaProperties GetParaProperties;
    public PtsDelegates.CreateParaclient CreateParaclient;
    public PtsDelegates.TransferDisplayInfo TransferDisplayInfo;
    public PtsDelegates.DestroyParaclient DestroyParaclient;

    // cbktxt
    public PtsDelegates.GetTextProperties GetTextProperties;
    public PtsDelegates.GetNumberFootnotes GetNumberFootnotes;
    public PtsDelegates.GetFootnotes GetFootnotes;
    public PtsDelegates.FormatLine FormatLine;
    public PtsDelegates.FormatLineForced FormatLineForced;
    public PtsDelegates.DestroyLine DestroyLine;
    public PtsDelegates.DestroyLineBreakRecord DestroyLineBreakRecord;
    public PtsDelegates.DestroyMcsclient DestroyMcsclient;
    public PtsDelegates.GetDvrSuppressibleBottomSpace GetDvrSuppressibleBottomSpace;
    public PtsDelegates.GetDvrAdvance GetDvrAdvance;
    public PtsDelegates.FInterruptFormattingText FInterruptFormattingText;

    public readonly bool Equals(FsContextInfo other)
    {
        return Version == other.Version && Fsffi == other.Fsffi && DrMinColumnBalancingStep == other.DrMinColumnBalancingStep && CInstalledObjects == other.CInstalledObjects && PInstalledObjects == other.PInstalledObjects && PfsClient == other.PfsClient && PtsPenaltyModule == other.PtsPenaltyModule && GetPageDimensions == other.GetPageDimensions && GetNextSection == other.GetNextSection && GetSectionProperties == other.GetSectionProperties && GetJustificationProperties == other.GetJustificationProperties && GetMainTextSegment == other.GetMainTextSegment && GetHeaderSegment == other.GetHeaderSegment && GetFooterSegment == other.GetFooterSegment && GetSectionColumnInfo == other.GetSectionColumnInfo && GetSegmentDefinedColumnSpanAreaInfo == other.GetSegmentDefinedColumnSpanAreaInfo && GetHeightDefinedColumnSpanAreaInfo == other.GetHeightDefinedColumnSpanAreaInfo && GetFirstPara == other.GetFirstPara && GetNextPara == other.GetNextPara && UpdGetSegmentChange == other.UpdGetSegmentChange && GetParaProperties == other.GetParaProperties && CreateParaclient == other.CreateParaclient && TransferDisplayInfo == other.TransferDisplayInfo && DestroyParaclient == other.DestroyParaclient && GetTextProperties == other.GetTextProperties && GetNumberFootnotes == other.GetNumberFootnotes && GetFootnotes == other.GetFootnotes && FormatLine == other.FormatLine && FormatLineForced == other.FormatLineForced && DestroyLine == other.DestroyLine && DestroyLineBreakRecord == other.DestroyLineBreakRecord && GetDvrSuppressibleBottomSpace == other.GetDvrSuppressibleBottomSpace && GetDvrAdvance == other.GetDvrAdvance && FInterruptFormattingText == other.FInterruptFormattingText;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsContextInfo other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Version);
        h.Add(Fsffi);
        h.Add(DrMinColumnBalancingStep);
        h.Add(CInstalledObjects);
        h.Add(PInstalledObjects);
        h.Add(PfsClient);
        h.Add(PtsPenaltyModule);
        h.Add(GetPageDimensions);
        h.Add(GetNextSection);
        h.Add(GetSectionProperties);
        h.Add(GetJustificationProperties);
        h.Add(GetMainTextSegment);
        h.Add(GetHeaderSegment);
        h.Add(GetFooterSegment);
        h.Add(GetSectionColumnInfo);
        h.Add(GetSegmentDefinedColumnSpanAreaInfo);
        h.Add(GetHeightDefinedColumnSpanAreaInfo);
        h.Add(GetFirstPara);
        h.Add(GetNextPara);
        h.Add(UpdGetSegmentChange);
        h.Add(GetParaProperties);
        h.Add(CreateParaclient);
        h.Add(TransferDisplayInfo);
        h.Add(DestroyParaclient);
        h.Add(GetTextProperties);
        h.Add(GetNumberFootnotes);
        h.Add(GetFootnotes);
        h.Add(FormatLine);
        h.Add(FormatLineForced);
        h.Add(DestroyLine);
        h.Add(DestroyLineBreakRecord);
        h.Add(GetDvrSuppressibleBottomSpace);
        h.Add(GetDvrAdvance);
        h.Add(FInterruptFormattingText);
        return h.ToHashCode();
    }

    public static bool operator ==(FsContextInfo left, FsContextInfo right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsContextInfo left, FsContextInfo right)
    {
        return !left.Equals(right);
    }
}
