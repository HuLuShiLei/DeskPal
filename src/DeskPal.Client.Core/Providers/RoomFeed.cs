using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Providers;

/// <summary>
/// The "room" line DeskPal writes to the standard input of a plugin that reads <see cref="ProviderReads.Room"/>:
/// the current room and, for each member, who they are and their state.
/// <para>
/// Only the fields below are written, copied one by one; nothing is serialized whole. Titles never reach a plugin:
/// every window, page, plugin-supplied and media title of a member is in <see cref="PresenceSnapshot.Detail"/>, which
/// is not read here, even when the member shows details to the room. <see cref="PresenceSnapshot.CustomText"/> and the
/// app or site name (<see cref="PresenceSnapshot.AppDisplayName"/>) are not read either.
/// </para>
/// Member fields: id, name, owner, me, blocked, and unless blocked: state, category, since, client.
/// </summary>
public static class RoomFeed
{
    public const string Type = "room";

    private static readonly JsonWriterOptions Options = new()
    {
        // Names stay readable for plugins in any language; this is a line on a pipe, not HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="room">The room I am in; null when in none.</param>
    /// <param name="presenceOf">The latest snapshot a member sent to this room; null when none arrived.</param>
    /// <param name="mine">What I sent to this room (after <c>PrivacyFilter.Apply</c>); null before anything was sent.</param>
    public static string Build(RoomDto? room, Guid myUserId, Func<Guid, PresenceSnapshot?> presenceOf, PresenceSnapshot? mine)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            json.WriteStartObject();
            json.WriteNumber("v", ProviderProtocol.Version);
            json.WriteString("type", Type);
            if (room is null)
            {
                json.WriteNull("room");
                json.WriteStartArray("members");
                json.WriteEndArray();
                json.WriteEndObject();
            }
            else
            {
                json.WriteStartObject("room");
                json.WriteString("id", room.Id);
                json.WriteString("name", room.Name);
                json.WriteEndObject();
                json.WriteStartArray("members");
                foreach (var member in room.Members)
                {
                    var me = member.UserId == myUserId;
                    var blocked = !me && room.Blocked?.Contains(member.UserId) == true;
                    json.WriteStartObject();
                    json.WriteString("id", member.UserId);
                    json.WriteString("name", member.DisplayName);
                    json.WriteBoolean("owner", member.Role == RoomRole.Owner);
                    json.WriteBoolean("me", me);
                    json.WriteBoolean("blocked", blocked);
                    if (!blocked)
                    {
                        WriteState(json, me ? mine : presenceOf(member.UserId));
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The state fields of one member. Keep this to fields without any title in them.</summary>
    private static void WriteState(Utf8JsonWriter json, PresenceSnapshot? snapshot)
    {
        json.WriteString("state", (snapshot?.State ?? PresenceState.Offline).ToString());
        json.WriteString("category", (snapshot?.Category ?? ActivityCategory.None).ToString());
        if (snapshot is null)
        {
            json.WriteNull("since");
        }
        else
        {
            json.WriteString("since", snapshot.Since.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (snapshot?.State is null or PresenceState.Offline || snapshot.Client is null)
        {
            json.WriteNull("client");
        }
        else
        {
            json.WriteString("client", snapshot.Client);
        }
    }
}
