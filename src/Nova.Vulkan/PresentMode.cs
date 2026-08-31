using JetBrains.Annotations;

namespace Nova.Vulkan;

[PublicAPI]
public enum PresentMode
{
    Fifo,
    Mailbox,
    Immediate
}
