using System.Collections.Concurrent;
using System.Security.Cryptography;
using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Stats;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>The message shown to the user when a member's stats cannot be fetched.</summary>
public sealed class StatsUnavailableException(string message) : Exception(message);

/// <summary>
/// Asks room members for their usage stats and answers their requests. Stats go only from one member to the other,
/// encrypted with one-time keys; the server forwards them without reading.
/// </summary>
public sealed class StatsExchange(HubService hub, RoomState rooms, UsageService usage, ILogger<StatsExchange> logger)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    /// <summary>Paging through a member's stats records one view, not one per page.</summary>
    private static readonly TimeSpan ViewCountedFor = TimeSpan.FromMinutes(10);

    /// <summary>Kept below the server's 128 KB message limit.</summary>
    private const int MaxCipherChars = 100 * 1024;

    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();
    private readonly ConcurrentDictionary<(Guid, DateOnly, DateOnly, DateOnly?), (DateTimeOffset At, UsageReport Report)> _received = new();
    private readonly ConcurrentDictionary<(Guid, Guid, DateOnly, DateOnly, DateOnly?), (DateTimeOffset At, UsageReport Report)> _answered = new();
    private readonly ConcurrentDictionary<(Guid, Guid), DateTimeOffset> _viewCounted = new();

    public void Handle(Envelope envelope)
    {
        if (envelope.To != rooms.MyUserId)
        {
            return;
        }

        switch (envelope.Type)
        {
            case MessageTypes.StatsRequest:
                _ = AnswerAsync(envelope);
                break;
            case MessageTypes.StatsResponse:
                Complete(envelope);
                break;
        }
    }

    /// <exception cref="StatsUnavailableException">Not connected, no answer in time, or the member does not share.</exception>
    public async Task<UsageReport> RequestAsync(Guid roomId, Guid userId, DateOnly from, DateOnly to, (DateOnly From, DateOnly To)? previous)
    {
        var cacheKey = (userId, from, to, previous?.From);
        if (_received.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.At < CacheFor)
        {
            return cached.Report;
        }

        using var key = new StatsCrypto.RequestKey();
        var requestId = Guid.NewGuid();
        var pending = new Pending(roomId, userId, key, new TaskCompletionSource<UsageReport>(TaskCreationOptions.RunContinuationsAsynchronously));
        _pending[requestId] = pending;
        try
        {
            var request = Envelope.Create(MessageTypes.StatsRequest, roomId, new StatsRequestPayload(requestId, from, to, key.PublicKey, previous?.From, previous?.To)) with { To = userId };
            if (!await hub.TryPublishAsync(request))
            {
                throw new StatsUnavailableException(Strings.Stats_NotConnected);
            }

            UsageReport report;
            try
            {
                report = await pending.Done.Task.WaitAsync(Timeout);
            }
            catch (TimeoutException)
            {
                throw new StatsUnavailableException(Strings.Stats_NoResponse);
            }

            _received[cacheKey] = (DateTimeOffset.UtcNow, report);
            return report;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private void Complete(Envelope envelope)
    {
        var response = envelope.ReadPayload<StatsResponsePayload>();
        if (response is null || !_pending.TryGetValue(response.RequestId, out var pending)
            || pending.UserId != envelope.SenderId || pending.RoomId != envelope.RoomId)
        {
            return;
        }

        if (response.Error is not null || response.ErrorCode is not null)
        {
            pending.Done.TrySetException(new StatsUnavailableException(ErrorText(response, pending)));
            return;
        }

        try
        {
            pending.Done.TrySetResult(pending.Key.Decrypt(response));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or InvalidDataException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Stats response could not be read ({Error})", ex.GetType().Name);
            pending.Done.TrySetException(new StatsUnavailableException(Strings.Stats_DecryptFailed));
        }
    }

    /// <summary>Older clients send only the text, in their own language.</summary>
    private string ErrorText(StatsResponsePayload response, Pending pending) => response.ErrorCode switch
    {
        StatsErrorCodes.NotShared => Strings.Stats_PeerNotSharing,
        StatsErrorCodes.OutOfRange => UiLanguage.Format(Strings.Stats_PeerSharedDays, rooms.Rooms.FirstOrDefault(r => r.Id == pending.RoomId)?.Members.FirstOrDefault(m => m.UserId == pending.UserId)?.StatsDays ?? 0),
        _ => response.Error ?? Strings.Stats_NoResponse,
    };

    /// <summary>
    /// Checked again here, whatever the server says: I am in this room right now, I share stats there, and the
    /// requester is a member I have not blocked. Requests that fail the room checks get no answer at all.
    /// </summary>
    private async Task AnswerAsync(Envelope envelope)
    {
        try
        {
            var request = envelope.ReadPayload<StatsRequestPayload>();
            var room = rooms.SelectedRoom;
            var requester = room?.Members.FirstOrDefault(m => m.UserId == envelope.SenderId);
            if (request is null || room is null || room.Id != envelope.RoomId || requester is null
                || requester.UserId == rooms.MyUserId || room.Blocked?.Contains(requester.UserId) == true)
            {
                return;
            }

            var privacy = rooms.GetPrivacy(room.Id);
            if (!privacy.ShareStats)
            {
                await ReplyAsync(envelope, new StatsResponsePayload(request.RequestId, Error: Strings.Stats_PeerNotSharing, ErrorCode: StatsErrorCodes.NotShared));
                return;
            }

            var today = UsageService.Today;
            var first = today.AddDays(1 - privacy.StatsDays);
            var from = request.From < first ? first : request.From;
            var to = request.To > today ? today : request.To;
            if (from > to)
            {
                await ReplyAsync(envelope, new StatsResponsePayload(request.RequestId, Error: UiLanguage.Format(Strings.Stats_PeerSharedDays, privacy.StatsDays), ErrorCode: StatsErrorCodes.OutOfRange));
                return;
            }

            (DateOnly, DateOnly)? previous = request is { PreviousFrom: { } pf, PreviousTo: { } pt } && pf >= first && pt < from ? (pf, pt) : null;
            var cacheKey = (requester.UserId, room.Id, from, to, previous?.Item1);
            UsageReport report;
            if (_answered.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.At < CacheFor)
            {
                report = cached.Report;
            }
            else
            {
                var full = await Task.Run(() => usage.Report(from, to, previous));
                report = StatsFilter.Apply(full, privacy) with { SharedDays = privacy.StatsDays };
                _answered[cacheKey] = (DateTimeOffset.UtcNow, report);
            }

            var payload = StatsCrypto.Encrypt(request.RequestId, request.PublicKey, report);
            if (payload.Cipher!.Length > MaxCipherChars)
            {
                payload = StatsCrypto.Encrypt(request.RequestId, request.PublicKey, report with { Titles = report.Titles.Take(5).ToList(), Timeline = [] });
            }

            await ReplyAsync(envelope, payload, EncodingKinds.EcdhAesGcm);
            CountView(room.Id, room.Name, requester.UserId, requester.DisplayName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Answering a stats request failed");
        }
    }

    private Task<bool> ReplyAsync(Envelope request, StatsResponsePayload payload, string enc = EncodingKinds.None) =>
        hub.TryPublishAsync(Envelope.Create(MessageTypes.StatsResponse, request.RoomId, payload) with { To = request.SenderId, Enc = enc });

    private void CountView(Guid roomId, string roomName, Guid viewerId, string viewerName)
    {
        var now = DateTimeOffset.UtcNow;
        if (_viewCounted.TryGetValue((roomId, viewerId), out var last) && now - last < ViewCountedFor)
        {
            return;
        }

        _viewCounted[(roomId, viewerId)] = now;
        usage.AddView(new StatsViewRecord(roomId, roomName, viewerId, viewerName, now));
    }

    private sealed record Pending(Guid RoomId, Guid UserId, StatsCrypto.RequestKey Key, TaskCompletionSource<UsageReport> Done);
}