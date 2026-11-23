using Tmds.DBus.Protocol;

namespace DeskPal.Client.Native;

internal delegate void BodyWriter(ref MessageWriter writer);

/// <summary>Small helpers over the D-Bus session bus: method calls with a timeout and reading properties.</summary>
internal static class Bus
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DBusConnection? _session;

    /// <summary>The shared session bus connection, opened on first use. Throws when there is no session bus.</summary>
    public static async Task<DBusConnection> SessionAsync()
    {
        if (_session is not null)
        {
            return _session;
        }

        await Gate.WaitAsync();
        try
        {
            if (_session is null)
            {
                var address = DBusAddress.Session ?? throw new InvalidOperationException("No D-Bus session bus (DBUS_SESSION_BUS_ADDRESS is not set).");
                var connection = new DBusConnection(address);
                await connection.ConnectAsync();
                _session = connection;
            }

            return _session;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<T> CallAsync<T>(
        DBusConnection connection,
        string destination,
        string path,
        string @interface,
        string member,
        MessageValueReader<T> reader,
        string? signature = null,
        BodyWriter? body = null)
    {
        var message = Build(connection, destination, path, @interface, member, signature, body);

        return await connection.CallMethodAsync(message, reader).WaitAsync(CallTimeout);
    }

    public static async Task CallAsync(
        DBusConnection connection,
        string destination,
        string path,
        string @interface,
        string member,
        string? signature = null,
        BodyWriter? body = null)
    {
        var message = Build(connection, destination, path, @interface, member, signature, body);

        await connection.CallMethodAsync(message).WaitAsync(CallTimeout);
    }

    private static MessageBuffer Build(DBusConnection connection, string destination, string path, string @interface, string member, string? signature, BodyWriter? body)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination, path, @interface, member, signature);
            body?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>org.freedesktop.DBus.Properties.Get.</summary>
    public static Task<VariantValue> GetPropertyAsync(DBusConnection connection, string destination, string path, string @interface, string property) =>
        CallAsync(
            connection,
            destination,
            path,
            "org.freedesktop.DBus.Properties",
            "Get",
            static (Message m, object? _) => m.GetBodyReader().ReadVariantValue(),
            "ss",
            (ref MessageWriter w) =>
            {
                w.WriteString(@interface);
                w.WriteString(property);
            });

    /// <summary>The process that owns a bus name, or 0.</summary>
    public static async Task<uint> ProcessIdOfAsync(DBusConnection connection, string busName)
    {
        try
        {
            return await CallAsync(
                connection,
                "org.freedesktop.DBus",
                "/org/freedesktop/DBus",
                "org.freedesktop.DBus",
                "GetConnectionUnixProcessID",
                static (Message m, object? _) => m.GetBodyReader().ReadUInt32(),
                "s",
                (ref MessageWriter w) => w.WriteString(busName));
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
