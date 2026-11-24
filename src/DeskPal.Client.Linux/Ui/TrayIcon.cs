using System.Reflection;
using DeskPal.Client.Native;
using DeskPal.Client.Platform;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace DeskPal.Client.Ui;

/// <summary>
/// The tray icon, as a StatusNotifierItem on the session bus with a com.canonical.dbusmenu menu (Open, Quit).
/// KDE, Xfce, Cinnamon and others show it themselves; GNOME needs the AppIndicator extension, which Ubuntu ships
/// turned on. GTK 4 has no tray API and the usual helper libraries are GTK 3, so the two small interfaces are
/// implemented here. Without a tray host the icon is simply not shown; opening DeskPal again shows the panel.
/// </summary>
public sealed class TrayIcon
{
    private const string ItemPath = "/StatusNotifierItem";
    private const string MenuPath = "/MenuBar";
    private const string ItemInterface = "org.kde.StatusNotifierItem";
    private const string MenuInterface = "com.canonical.dbusmenu";
    private const string Watcher = "org.kde.StatusNotifierWatcher";
    private const string WatcherPath = "/StatusNotifierWatcher";
    private const int OpenId = 1;
    private const int SeparatorId = 2;
    private const int QuitId = 3;
    private static readonly int[] IconSizes = [22, 24, 32, 48];

    private readonly ILogger _logger;
    private readonly string _toolTip;
    private readonly List<(int Size, byte[] Data)> _icons = [];
    private DBusConnection? _connection;
    private IDisposable? _watch;
    private uint _revision = 1;

    public TrayIcon(string toolTip, ILogger logger)
    {
        _toolTip = toolTip;
        _logger = logger;
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var size in IconSizes)
        {
            using var stream = assembly.GetManifestResourceStream($"tray-{size}.argb");
            if (stream is not null)
            {
                var data = new byte[stream.Length];
                stream.ReadExactly(data);
                _icons.Add((size, data));
            }
        }
    }

    /// <summary>Left click on hosts that pass it on, and Open in the menu. Raised on the GTK thread.</summary>
    public event Action? Activated;

    /// <summary>Quit in the menu. Raised on the GTK thread.</summary>
    public event Action? QuitRequested;

    /// <summary>A tray host took the icon.</summary>
    public bool Registered { get; private set; }

    public async Task StartAsync()
    {
        try
        {
            var connection = await Bus.SessionAsync();
            _connection = connection;
            connection.AddMethodHandlers([new Handler(ItemPath, HandleItem), new Handler(MenuPath, HandleMenu)]);
            _watch = await connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Sender = "org.freedesktop.DBus", Interface = "org.freedesktop.DBus", Member = "NameOwnerChanged", Arg0 = Watcher },
                static (Message m, object? _) =>
                {
                    var reader = m.GetBodyReader();
                    reader.ReadString();
                    reader.ReadString();
                    return reader.ReadString();
                },
                (Notification<string> notification) =>
                {
                    // The tray host restarted (the shell, or the panel): register again.
                    if (notification.HasValue && notification.Value.Length > 0)
                    {
                        _ = RegisterAsync();
                    }
                },
                emitOnCapturedContext: false,
                ObserverFlags.None);
            await RegisterAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The tray icon could not be set up");
        }
    }

    /// <summary>Menu text follows the language chosen in Settings.</summary>
    public void RefreshMenu()
    {
        if (_connection is null)
        {
            return;
        }

        _revision++;
        Signal(MenuPath, MenuInterface, "LayoutUpdated", "ui", (ref MessageWriter w) =>
        {
            w.WriteUInt32(_revision);
            w.WriteInt32(0);
        });
    }

    public void Stop()
    {
        _watch?.Dispose();
        _watch = null;
        if (_connection is { } connection)
        {
            connection.RemoveMethodHandlers([ItemPath, MenuPath]);
            _connection = null;
        }
    }

    private async Task RegisterAsync()
    {
        if (_connection is not { } connection)
        {
            return;
        }

        try
        {
            await Bus.CallAsync(connection, Watcher, WatcherPath, Watcher, "RegisterStatusNotifierItem", "s", (ref MessageWriter w) => w.WriteString(ItemPath));
            Registered = true;
            _logger.LogInformation("Tray icon registered");
        }
        catch (Exception ex)
        {
            Registered = false;
            _logger.LogInformation("No tray host on this desktop ({Reason}); the tray icon is not shown", ex.Message);
        }
    }

    private void HandleItem(MethodContext context)
    {
        var request = context.Request;
        if (context.IsPropertiesInterfaceRequest)
        {
            ReplyProperties(context, ItemInterface, WriteItemProperty, ["Category", "Id", "Title", "Status", "WindowId", "IconName", "IconPixmap", "ToolTip", "ItemIsMenu", "Menu"]);
            return;
        }

        if (request.InterfaceAsString != ItemInterface)
        {
            context.ReplyUnknownMethodError();
            return;
        }

        switch (request.MemberAsString)
        {
            case "Activate":
                MainThread.Post(() => Activated?.Invoke());
                break;
            case "SecondaryActivate" or "ContextMenu" or "Scroll":
                break;
            default:
                context.ReplyUnknownMethodError();
                return;
        }

        ReplyEmpty(context);
    }

    private bool WriteItemProperty(ref MessageWriter w, string name)
    {
        switch (name)
        {
            case "Category": w.WriteVariantString("ApplicationStatus"); return true;
            case "Id": w.WriteVariantString("deskpal"); return true;
            case "Title": w.WriteVariantString("DeskPal"); return true;
            case "Status": w.WriteVariantString("Active"); return true;
            case "WindowId": w.WriteVariantUInt32(0); return true;
            case "IconName": w.WriteVariantString(""); return true;
            case "ItemIsMenu": w.WriteVariantBool(false); return true;
            case "Menu": w.WriteVariantObjectPath(MenuPath); return true;
            case "IconPixmap":
                w.WriteSignature("a(iiay)");
                WritePixmaps(ref w, _icons);
                return true;
            case "ToolTip":
                w.WriteSignature("(sa(iiay)ss)");
                w.WriteStructureStart();
                w.WriteString("");
                WritePixmaps(ref w, []);
                w.WriteString(_toolTip);
                w.WriteString("");
                return true;
            default:
                return false;
        }
    }

    private static void WritePixmaps(ref MessageWriter w, List<(int Size, byte[] Data)> icons)
    {
        var array = w.WriteArrayStart(DBusType.Struct);
        foreach (var (size, data) in icons)
        {
            w.WriteStructureStart();
            w.WriteInt32(size);
            w.WriteInt32(size);
            w.WriteArray(data);
        }

        w.WriteArrayEnd(array);
    }

    private void HandleMenu(MethodContext context)
    {
        var request = context.Request;
        if (context.IsPropertiesInterfaceRequest)
        {
            ReplyProperties(context, MenuInterface, WriteMenuProperty, ["Version", "TextDirection", "Status", "IconThemePath"]);
            return;
        }

        if (request.InterfaceAsString != MenuInterface)
        {
            context.ReplyUnknownMethodError();
            return;
        }

        var reader = request.GetBodyReader();
        switch (request.MemberAsString)
        {
            case "GetLayout":
            {
                var parent = reader.ReadInt32();
                var w = context.CreateReplyWriter("u(ia{sv}av)");
                try
                {
                    w.WriteUInt32(_revision);
                    WriteMenuItem(ref w, parent, withChildren: true);
                    context.Reply(w.CreateMessage());
                }
                finally
                {
                    w.Dispose();
                }

                return;
            }

            case "GetGroupProperties":
            {
                var ids = reader.ReadArrayOfInt32();
                var w = context.CreateReplyWriter("a(ia{sv})");
                try
                {
                    var array = w.WriteArrayStart(DBusType.Struct);
                    foreach (var id in ids.Length == 0 ? [0, OpenId, SeparatorId, QuitId] : ids)
                    {
                        w.WriteStructureStart();
                        w.WriteInt32(id);
                        WriteMenuProperties(ref w, id);
                    }

                    w.WriteArrayEnd(array);
                    context.Reply(w.CreateMessage());
                }
                finally
                {
                    w.Dispose();
                }

                return;
            }

            case "GetProperty":
            {
                var id = reader.ReadInt32();
                var name = reader.ReadString();
                var w = context.CreateReplyWriter("v");
                try
                {
                    switch (name)
                    {
                        case "label": w.WriteVariantString(Label(id)); break;
                        case "type": w.WriteVariantString(id == SeparatorId ? "separator" : "standard"); break;
                        case "children-display": w.WriteVariantString(id == 0 ? "submenu" : ""); break;
                        default: w.WriteVariantBool(true); break;
                    }

                    context.Reply(w.CreateMessage());
                }
                finally
                {
                    w.Dispose();
                }

                return;
            }

            case "Event":
            {
                var id = reader.ReadInt32();
                var kind = reader.ReadString();
                if (kind == "clicked")
                {
                    Clicked(id);
                }

                ReplyEmpty(context);
                return;
            }

            case "EventGroup":
            {
                var end = reader.ReadArrayStart(DBusType.Struct);
                while (reader.HasNext(end))
                {
                    reader.AlignStruct();
                    var id = reader.ReadInt32();
                    var kind = reader.ReadString();
                    reader.ReadVariantValue();
                    reader.ReadUInt32();
                    if (kind == "clicked")
                    {
                        Clicked(id);
                    }
                }

                var w = context.CreateReplyWriter("ai");
                try
                {
                    w.WriteArrayEnd(w.WriteArrayStart(DBusType.Int32));
                    context.Reply(w.CreateMessage());
                }
                finally
                {
                    w.Dispose();
                }

                return;
            }

            case "AboutToShow":
            {
                var w = context.CreateReplyWriter("b");
                try
                {
                    w.WriteBool(false);
                    context.Reply(w.CreateMessage());
                }
                finally
                {
                    w.Dispose();
                }

                return;
            }

            case "AboutToShowGroup":
            {
                var w = context.CreateReplyWriter("aiai");
                try
                {
                    w.WriteArrayEnd(w.WriteArrayStart(DBusType.Int32));
                    w.WriteArrayEnd(w.WriteArrayStart(DBusType.Int32));
                    context.Reply(w.CreateMessage());
                }
                finally
                {
                    w.Dispose();
                }

                return;
            }

            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }

    private void Clicked(int id)
    {
        switch (id)
        {
            case OpenId:
                MainThread.Post(() => Activated?.Invoke());
                break;
            case QuitId:
                MainThread.Post(() => QuitRequested?.Invoke());
                break;
        }
    }

    private static bool WriteMenuProperty(ref MessageWriter w, string name)
    {
        switch (name)
        {
            case "Version": w.WriteVariantUInt32(3); return true;
            case "TextDirection": w.WriteVariantString("ltr"); return true;
            case "Status": w.WriteVariantString("normal"); return true;
            case "IconThemePath":
                w.WriteSignature("as");
                w.WriteArrayEnd(w.WriteArrayStart(DBusType.String));
                return true;
            default:
                return false;
        }
    }

    /// <summary>(ia{sv}av): id, properties, children (each a variant holding the same structure).</summary>
    private static void WriteMenuItem(ref MessageWriter w, int id, bool withChildren)
    {
        w.WriteStructureStart();
        w.WriteInt32(id);
        WriteMenuProperties(ref w, id);
        var children = w.WriteArrayStart(DBusType.Variant);
        if (id == 0 && withChildren)
        {
            foreach (var child in new[] { OpenId, SeparatorId, QuitId })
            {
                w.WriteSignature("(ia{sv}av)");
                WriteMenuItem(ref w, child, withChildren: false);
            }
        }

        w.WriteArrayEnd(children);
    }

    private static void WriteMenuProperties(ref MessageWriter w, int id)
    {
        var dict = w.WriteDictionaryStart();
        if (id == 0)
        {
            w.WriteDictionaryEntryStart();
            w.WriteString("children-display");
            w.WriteVariantString("submenu");
        }
        else if (id == SeparatorId)
        {
            w.WriteDictionaryEntryStart();
            w.WriteString("type");
            w.WriteVariantString("separator");
        }
        else
        {
            w.WriteDictionaryEntryStart();
            w.WriteString("label");
            w.WriteVariantString(Label(id));
            w.WriteDictionaryEntryStart();
            w.WriteString("enabled");
            w.WriteVariantBool(true);
            w.WriteDictionaryEntryStart();
            w.WriteString("visible");
            w.WriteVariantBool(true);
        }

        w.WriteDictionaryEnd(dict);
    }

    private static string Label(int id) => id switch
    {
        OpenId => Strings.Tray_Open,
        QuitId => Strings.Tray_Exit,
        _ => "",
    };

    private delegate bool PropertyWriter(ref MessageWriter writer, string name);

    /// <summary>org.freedesktop.DBus.Properties Get and GetAll for one interface.</summary>
    private static void ReplyProperties(MethodContext context, string @interface, PropertyWriter write, string[] names)
    {
        var request = context.Request;
        var reader = request.GetBodyReader();
        switch (request.MemberAsString)
        {
            case "Get":
            {
                var requested = reader.ReadString();
                var name = reader.ReadString();
                var w = context.CreateReplyWriter("v");
                try
                {
                    if (requested != @interface || !write(ref w, name))
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", name);
                        return;
                    }

                    context.Reply(w.CreateMessage());
                }
                finally
                {
                    w.Dispose();
                }

                return;
            }

            case "GetAll":
            {
                var w = context.CreateReplyWriter("a{sv}");
                try
                {
                    var dict = w.WriteDictionaryStart();
                    foreach (var name in names)
                    {
                        w.WriteDictionaryEntryStart();
                        w.WriteString(name);
                        write(ref w, name);
                    }

                    w.WriteDictionaryEnd(dict);
                    context.Reply(w.CreateMessage());
                }
                finally
                {
                    w.Dispose();
                }

                return;
            }

            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }

    private static void ReplyEmpty(MethodContext context)
    {
        var w = context.CreateReplyWriter(null!);
        try
        {
            context.Reply(w.CreateMessage());
        }
        finally
        {
            w.Dispose();
        }
    }

    private void Signal(string path, string @interface, string member, string signature, BodyWriter body)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        var w = connection.GetMessageWriter();
        try
        {
            w.WriteSignalHeader(null!, path, @interface, member, signature);
            body(ref w);
            connection.TrySendMessage(w.CreateMessage());
        }
        finally
        {
            w.Dispose();
        }
    }

    private sealed class Handler(string path, Action<MethodContext> handle) : IPathMethodHandler
    {
        public string Path => path;

        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml([], (IList<string>)[]);
                return default;
            }

            handle(context);
            return default;
        }
    }
}
