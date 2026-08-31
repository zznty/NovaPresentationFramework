using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using Silk.NET.SDL;
using SdlApi = Silk.NET.SDL.Sdl;

namespace Nova.Sdl;

/// <summary>The message-box icon flavors SDL can show.</summary>
public enum MessageBoxIconKind
{
    None = 0,
    Error = 1,
    Warning = 2,
    Information = 3,
}

/// <summary>One message-box button. The id should be a caller-meaningful value (SDL returns
/// the pressed button's id; ids must be positive and unique within the box).</summary>
/// <param name="Id">Positive unique id, returned when the button is pressed.</param>
/// <param name="Label">The visible label.</param>
/// <param name="IsDefault">Marks the return-key default (SDL_MESSAGEBOX_BUTTON_RETURNKEY_DEFAULT).</param>
/// <param name="IsEscape">Marks the escape-key default (SDL_MESSAGEBOX_BUTTON_ESCAPEKEY_DEFAULT).</param>
[PublicAPI]
public sealed record MessageBoxButtonDefinition(int Id, string Label, bool IsDefault, bool IsEscape);

/// <summary>
/// SDL3 message boxes. On Linux the backend spawns zenity (the XDG Desktop Portal has no
/// message-box interface, so SDL falls back to a subprocess — see SDL_zenitymessagebox.c);
/// the call is synchronous, like the Win32 MessageBox it replaces.
/// </summary>
[PublicAPI]
public static class SdlMessageBox
{
    private const uint ReturnKeyDefault = 0x00000001;

    private static uint IconFlags(MessageBoxIconKind icon)
    {
        return icon switch
        {
            MessageBoxIconKind.None => 0,
            MessageBoxIconKind.Error => 0x10,
            MessageBoxIconKind.Warning => 0x20,
            MessageBoxIconKind.Information => 0x40,
            _ => 0,
        };
    }
    private const uint EscapeKeyDefault = 0x00000002;

    /// <summary>
    /// Shows a synchronous message box. Returns the pressed button's id, or <see langword="null"/>
    /// when the dialog could not be shown (e.g. no zenity on the system).
    /// </summary>
    public static int? Show(
        SdlWindow? parent,
        string? title,
        string message,
        MessageBoxIconKind icon,
        IReadOnlyList<MessageBoxButtonDefinition> buttons)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(buttons);
        if (buttons.Count == 0)
        {
            return null;
        }

        unsafe
        {
            // SDL_ShowMessageBox is synchronous: the buffers only need to live for the call
            // itself. Title/message/native-struct sit in fixed locals; the per-button label
            // buffers are individually allocated, so each is pinned with a GCHandle for the
            // call's duration.
            byte[] titleUtf8 = title is null ? [0] : [.. Encoding.UTF8.GetBytes(title), 0];
            byte[] messageUtf8 = [.. Encoding.UTF8.GetBytes(message), 0];
            byte[][] labelUtf8 = new byte[buttons.Count][];
            for (int i = 0; i < buttons.Count; i++)
            {
                labelUtf8[i] = [.. Encoding.UTF8.GetBytes(buttons[i].Label), 0];
            }
            MessageBoxButtonData[] nativeButtons = new MessageBoxButtonData[buttons.Count];
            GCHandle[] labelPins = new GCHandle[labelUtf8.Length];
            try
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    MessageBoxButtonDefinition button = buttons[i];
                    uint flags = 0;
                    if (button.IsDefault)
                    {
                        flags |= ReturnKeyDefault;
                    }

                    if (button.IsEscape)
                    {
                        flags |= EscapeKeyDefault;
                    }

                    labelPins[i] = GCHandle.Alloc(labelUtf8[i], GCHandleType.Pinned);
                    nativeButtons[i] = new MessageBoxButtonData
                    {
                        Flags = flags,
                        ButtonID = button.Id,
                        Text = (sbyte*)labelPins[i].AddrOfPinnedObject(),
                    };
                }

                fixed (byte* titlePtr = titleUtf8)
                fixed (byte* messagePtr = messageUtf8)
                fixed (MessageBoxButtonData* buttonsPtr = nativeButtons)
                {
                    var data = new MessageBoxData
                    {
                        Flags = IconFlags(icon),
                        Window = new Silk.NET.SDL.WindowHandle((void*)(parent?.Handle.Value ?? 0)),
                        Title = (sbyte*)titlePtr,
                        Message = (sbyte*)messagePtr,
                        Numbuttons = nativeButtons.Length,
                        Buttons = buttonsPtr,
                    };

                    int buttonId = -1;
                    return SdlApi.ShowMessageBox(&data, &buttonId) != 0
                        ? buttonId
                        : null;
                }
            }
            finally
            {
                foreach (GCHandle pin in labelPins)
                {
                    if (pin.IsAllocated)
                    {
                        pin.Free();
                    }
                }
            }
        }
    }
}
