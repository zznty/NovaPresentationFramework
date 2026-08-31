using System.Windows.Media;
using System.Windows.Media.Composition;
using JetBrains.Annotations;
using Nova.Host;
using Nova.Mil;

namespace Nova.SdlSource;

/// <summary>
/// SDL-backed <see cref="CompositionTarget"/>. Registers with
/// <see cref="MediaContext"/>; MediaContext still drives
/// <c>ICompositionTarget.Render</c> on the base class.
/// </summary>
[PublicAPI]
public sealed class SdlCompositionTarget : CompositionTarget
{
    private Matrix _toDevice = Matrix.Identity;
    private Matrix _fromDevice = Matrix.Identity;

    internal SdlCompositionTarget(CompositionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Frame = frame;
        ApplyScale(frame.Window?.DisplayScale ?? 1.0);
        MediaContext.RegisterICompositionTarget(Dispatcher, this);
    }

    internal CompositionFrame Frame { get; }

    internal int BindingId { get; private set; } = -1;

    internal void DetachBinding()
    {
        if (BindingId >= 0)
        {
            DuceRuntime.Detach(BindingId);
            BindingId = -1;
        }
    }

    public override Matrix TransformToDevice => _toDevice;

    public override Matrix TransformFromDevice => _fromDevice;

    public override void Dispose()
    {
        VerifyAccess();
        if (!IsDisposed)
        {
            RootVisual = null!;
            DetachBinding();
            MediaContext.UnregisterICompositionTarget(Dispatcher, this);
        }

        base.Dispose();
    }

    internal void ApplyScale(double scale)
    {
        double s = scale > 0 ? scale : 1.0;
        _toDevice = new Matrix(s, 0, 0, s, 0, 0);
        _fromDevice = new Matrix(1.0 / s, 0, 0, 1.0 / s, 0, 0);
    }

    internal override void CreateUCEResources(DUCE.Channel channel, DUCE.Channel outOfBandChannel)
    {
        // Register the channel set BEFORE the base call so the out-of-band channel's commit
        // (content-root resource creation) routes into the shared graph. WPF shares one
        // channel set per MediaContext across every composition target, so the set owns one
        // graph and this target is multiplexed by its target resource handle.
        SlaveGraph graph = DuceRuntime.GetOrCreateChannelGraph(channel.ChannelHandle, outOfBandChannel.ChannelHandle, Frame.Graph);
        Frame.AdoptGraph(graph);
        BindingId = DuceRuntime.Attach(graph, Frame.Present);

        base.CreateUCEResources(channel, outOfBandChannel);

        bool created = _target.CreateOrAddRefOnChannel(this, channel, DUCE.ResourceType.TYPE_GENERICRENDERTARGET);
        // The graph keys each target's root by the TargetSetRoot TARGET handle — the
        // GENERICRENDERTARGET handle this target created on the shared channel.
        Frame.SetTargetHandle((uint)_target.GetHandle(channel));
        if (created)
        {
            DUCE.CompositionTarget.SetRoot(
                _target.GetHandle(channel),
                _contentRoot.GetHandle(channel),
                channel);
        }
    }

    internal override void ReleaseUCEResources(DUCE.Channel channel, DUCE.Channel outOfBandChannel)
    {
        // The channel set's shared graph outlives this target (other targets may be alive);
        // only this target's binding is released, from Dispose via DetachBinding.
        if (_target.IsOnChannel(channel))
        {
            DUCE.CompositionTarget.SetRoot(
                _target.GetHandle(channel),
                DUCE.ResourceHandle.Null,
                channel);
            _ = _target.ReleaseOnChannel(channel);
        }

        base.ReleaseUCEResources(channel, outOfBandChannel);
    }

    private DUCE.MultiChannelResource _target;
}
