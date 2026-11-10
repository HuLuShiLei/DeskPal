namespace DeskPal.Shared.Messaging;

public static class HubContract
{
    public const string Path = "/hubs/presence";

    /// <summary>Server to client: deliver one envelope.</summary>
    public const string Receive = "Receive";

    /// <summary>Client to server: publish one envelope to a Room.</summary>
    public const string Publish = "Publish";
}

public interface IPresenceClient
{
    Task Receive(Envelope envelope);
}