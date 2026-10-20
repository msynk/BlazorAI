using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazor.AI.Json;

/// <summary>
/// Serializes <see cref="AiRole"/> as its wire string (<c>"user"</c>) rather than as an object.
/// </summary>
public sealed class AiRoleJsonConverter : JsonConverter<AiRole>
{
    /// <inheritdoc />
    public override AiRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return string.IsNullOrWhiteSpace(value) ? AiRole.User : new AiRole(value);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, AiRole value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>
/// Serializes <see cref="ArtifactKind"/> as its wire string (<c>"code"</c>) rather than as an object.
/// </summary>
public sealed class ArtifactKindJsonConverter : JsonConverter<ArtifactKind>
{
    /// <inheritdoc />
    public override ArtifactKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return string.IsNullOrWhiteSpace(value) ? ArtifactKind.Document : new ArtifactKind(value);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ArtifactKind value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
