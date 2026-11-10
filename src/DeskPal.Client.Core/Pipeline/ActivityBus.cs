using System.Threading.Channels;

namespace DeskPal.Client.Core.Pipeline;

/// <summary>In-process fan-out bus: every subscriber gets its own channel with every published signal.</summary>
public sealed class ActivityBus
{
    private readonly List<Channel<ActivitySignal>> _subscribers = new();
    private readonly Lock _lock = new();

    public ChannelReader<ActivitySignal> Subscribe()
    {
        var channel = Channel.CreateBounded<ActivitySignal>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        return channel.Reader;
    }

    public void Publish(ActivitySignal signal)
    {
        lock (_lock)
        {
            foreach (var channel in _subscribers)
            {
                channel.Writer.TryWrite(signal);
            }
        }
    }
}