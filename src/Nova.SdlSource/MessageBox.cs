using JetBrains.Annotations;

namespace Nova.SdlSource;

/// <summary>The message-box icon flavor. Values mirror <c>Nova.Sdl.MessageBoxIconKind</c>.
/// Declared here so PresentationFramework (which cannot reference Nova.Sdl under Arcade's
/// disabled transitive project refs) can drive the dialog.</summary>
public enum MessageBoxIconKind
{
    None = 0,
    Error = 1,
    Warning = 2,
    Information = 3,
}

/// <summary>One message-box button; the id is returned when the button is pressed.</summary>
/// <param name="Id">Positive unique id, returned when the button is pressed.</param>
/// <param name="Label">The visible label.</param>
/// <param name="IsDefault">Marks the return-key default.</param>
/// <param name="IsEscape">Marks the escape-key default.</param>
[PublicAPI]
public sealed record MessageBoxButtonDefinition(int Id, string Label, bool IsDefault, bool IsEscape);
