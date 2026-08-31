using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace Nova.Serialization;

/// <summary>
/// JSON journal-value serializer for the WPF navigation journal's DP values: the Linux
/// replacement for the WindowsDesktop-only NRBF writer (<c>BinaryFormatWriter</c>), while
/// the NRBF reader stays available as a fallback for journal entries created on Windows.
/// The caller (MS.Internal.DataStreams) enforces the journalable-property rules; this type
/// only round-trips a single value with its runtime type, so a restored value matches the
/// dependency property's type exactly.
/// </summary>
[PublicAPI]
public static class JournalValueSerializer
{
    /// <summary>
    /// Writes <paramref name="value"/> tagged with its runtime type. Returns false when the
    /// type is not serializable (no public properties for the reflection serializer).
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based JSON serialization of the runtime value type.")]
    [RequiresDynamicCode("Reflection-based JSON serialization of the runtime value type.")]
    public static bool TryWrite(Stream stream, object value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            Type type = value.GetType();
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteString("t", type.AssemblyQualifiedName);
            writer.WritePropertyName("v");
            JsonSerializer.Serialize(writer, value, type);
            writer.WriteEndObject();
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (IsTolerable(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a value written by <see cref="TryWrite"/>. Returns false when the payload is
    /// not this format or the type cannot be resolved in this application.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based JSON deserialization of the runtime value type.")]
    [RequiresDynamicCode("Reflection-based JSON deserialization of the runtime value type.")]
    public static bool TryRead(Stream stream, out object? value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        value = null;
        try
        {
            byte[] bytes;
            if (stream is MemoryStream memoryStream)
            {
                bytes = memoryStream.GetBuffer().AsSpan(0, checked((int)memoryStream.Length)).ToArray();
            }
            else
            {
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                bytes = copy.ToArray();
            }

            var reader = new Utf8JsonReader(bytes);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("t"))
            {
                return false;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                return false;
            }

            string? typeName = reader.GetString();
            if (string.IsNullOrEmpty(typeName) || Type.GetType(typeName, throwOnError: false) is not { } type)
            {
                return false;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("v"))
            {
                return false;
            }

            if (!reader.Read())
            {
                return false;
            }

            value = JsonSerializer.Deserialize(ref reader, type);
            return value is not null || Nullable.GetUnderlyingType(type) is not null || type == typeof(string);
        }
        catch (Exception ex) when (IsTolerable(ex))
        {
            value = null;
            return false;
        }
    }

    private static bool IsTolerable(Exception ex)
    {
        return ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
    }
}
