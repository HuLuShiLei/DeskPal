using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using DeskPal.Shared.Messaging;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

public sealed class HubService(AppSettings settings, ILogger<HubService> logger) : IAsyncDisposable
{
    private HubConnection? _connection;
    private CancellationTokenSource? _cts;

    public event Action<Envelope>? Received;

    public event Action<HubConnectionState>? StateChanged;

    /// <summary>Raised after the first connect and after every reconnect; the server forgets nothing but our own presence must be re-sent.</summary>
    public event Func<Task>? Connected;

    /// <summary>The account signed in on another computer, or the token was rejected. The connection stops retrying.</summary>
    public event Action? SessionEnded;

    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _connection = new HubConnectionBuilder()
            .WithUrl(settings.ServerUrl.TrimEnd('/') + HubContract.Path, o => o.AccessTokenProvider = () => Task.FromResult(settings.GetToken()))
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        _connection.On<Envelope>(HubContract.Receive, envelope =>
        {
            if (envelope.Type == MessageTypes.SessionReplaced)
            {
                EndSession();
                return;
            }

            Received?.Invoke(envelope);
        });
        _connection.Reconnecting += _ => Notify(HubConnectionState.Reconnecting);
        _connection.Reconnected += async _ =>
        {
            await Notify(HubConnectionState.Connected);
            await RaiseConnected();
        };
        _connection.Closed += async error =>
        {
            await Notify(HubConnectionState.Disconnected);
            _ = ConnectLoopAsync(_cts.Token);
        };

        _ = ConnectLoopAsync(_cts.Token);
    }

    public async Task<bool> TryPublishAsync(Envelope envelope)
    {
        if (_connection?.State != HubConnectionState.Connected)
        {
            return false;
        }

        try
        {
            await _connection.InvokeAsync(HubContract.Publish, envelope);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Publishing {Type} failed ({Error})", envelope.Type, ex.GetType().Name);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    private async Task ConnectLoopAsync(CancellationToken token)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!token.IsCancellationRequested && _connection!.State == HubConnectionState.Disconnected)
        {
            try
            {
                await Notify(HubConnectionState.Connecting);
                await _connection.StartAsync(token);
                await Notify(HubConnectionState.Connected);
                await RaiseConnected();
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogInformation("Hub rejected the token");
                EndSession();
                return;
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                logger.LogInformation("Connecting to hub failed ({Error}), retrying in {Delay}", ex.GetType().Name, delay);
                await Notify(HubConnectionState.Disconnected);
                await Task.Delay(delay, token);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }

    private void EndSession()
    {
        if (_cts is not { IsCancellationRequested: false } cts)
        {
            return;
        }

        cts.Cancel();
        SessionEnded?.Invoke();
    }

    private Task Notify(HubConnectionState state)
    {
        logger.LogInformation("Hub {State}", state);
        StateChanged?.Invoke(state);
        return Task.CompletedTask;
    }

    private async Task RaiseConnected()
    {
        if (Connected is { } handler)
        {
            await handler();
        }
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext context) =>
            context.RetryReason is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }
                ? null
                : TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(context.PreviousRetryCount, 6))));
    }
}
