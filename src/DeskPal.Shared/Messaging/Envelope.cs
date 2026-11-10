using System.Text.Json;

namespace DeskPal.Shared.Messaging;

/// <summary>
/// Every realtime message uses this envelope. The server routes by Type and RoomId only and never reads Payload,
/// so Payload can become ciphertext later (Enc != "none") without changing the server.
/// </summary>
public sealed record Envelope
{
    public required string Type { get; init; }
    public int V { get; init; } = 1;
    public Guid RoomId { get; init; }
    public Guid SenderId { get; init; }
    public DateTimeOffset Ts { get; init; }
    public string Enc { get; init; } = EncodingKinds.None;
    public JsonElement Payload { get; init; }

    public static Envelope Create<T>(string type, Guid roomId, T payload) => new()
    {
        Type = type,
        RoomId = roomId,
        Ts = DateTimeOffset.UtcNow,
        Payload = JsonSerializer.SerializeToElement(payload, DeskPalJson.Options),
    };

    public T? ReadPayload<T>() => Payload.ValueKind == JsonValueKind.Undefined
        ? default
        : Payload.Deserialize<T>(DeskPalJson.Options);
}

public static class EncodingKinds
{
    public const string None = "none";
}