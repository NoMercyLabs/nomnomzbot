// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Vts.Entities;
using NomNomzBot.Domain.Vts.Events;
using NomNomzBot.Infrastructure.Obs.Transport;

namespace NomNomzBot.Infrastructure.Vts.Transport;

/// <summary>
/// The self-host VTS transport (vtube-studio.md §0 D1/D2): one WebSocket per channel to the
/// channel's OWN configured endpoint (no other host is ever dialed). Session start replays the
/// stored plugin token as an <c>AuthenticationRequest</c> BEFORE anything else — an unauthorized
/// session fails closed (<c>VTS_UNAUTHORIZED</c>, the streamer must re-approve) — then subscribes
/// the channel's masked event set. Requests correlate by <c>requestID</c>; an <c>APIError</c>
/// surfaces as the failure; inbound <c>*Event</c> frames publish <see cref="VtsEventReceived"/>.
/// Reuses the generic WS text seam (<see cref="IObsSocketFactory"/>) so tests drive the whole
/// protocol in-memory.
/// </summary>
public sealed class DirectVtsTransport : IVtsTransport, IAsyncDisposable, IDisposable
{
    public const string PluginName = "NomNomzBot";
    public const string PluginDeveloper = "NoMercy Labs";

    /// <summary>Mask-bit → VTS event name (vtube-studio.md §4); bits 8+ are the high-volume opt-ins.</summary>
    public static readonly string[] EventBits =
    [
        "ModelLoadedEvent",
        "TrackingStatusChangedEvent",
        "BackgroundChangedEvent",
        "ModelConfigChangedEvent",
        "HotkeyTriggeredEvent",
        "ModelClickedEvent",
        "ItemEvent",
        "PostProcessingEvent",
        "ModelMovedEvent",
        "ModelOutlineEvent",
        "ModelAnimationEvent",
    ];

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly IObsSocketFactory _socketFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;
    private readonly ILogger<DirectVtsTransport> _logger;

    private readonly ConcurrentDictionary<Guid, VtsSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _connectGates = new();

    public DirectVtsTransport(
        IObsSocketFactory socketFactory,
        IServiceScopeFactory scopeFactory,
        IEventBus eventBus,
        TimeProvider clock,
        ILogger<DirectVtsTransport> logger
    )
    {
        _socketFactory = socketFactory;
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _clock = clock;
        _logger = logger;
    }

    private sealed class VtsSession : IAsyncDisposable
    {
        public required IObsSocket Socket { get; init; }
        public required Task ReceiveLoop { get; set; }
        public ConcurrentDictionary<string, TaskCompletionSource<JsonDocument>> Pending { get; } =
            new();

        public ValueTask DisposeAsync() => Socket.DisposeAsync();
    }

    public async Task<Result<string>> RequestAsync(
        Guid broadcasterId,
        string requestType,
        string? dataJson,
        CancellationToken ct = default
    )
    {
        Result<VtsSession> session = await GetOrConnectAsync(broadcasterId, ct);
        if (session.IsFailure)
            return Result.Failure<string>(session.ErrorMessage!, session.ErrorCode!);

        Result<JsonDocument> reply = await RoundTripAsync(
            broadcasterId,
            session.Value,
            requestType,
            dataJson,
            ct
        );
        if (reply.IsFailure)
            return Result.Failure<string>(reply.ErrorMessage!, reply.ErrorCode!);
        using JsonDocument doc = reply.Value;
        return ExtractData(doc);
    }

    private static Result<string> ExtractData(JsonDocument doc)
    {
        string messageType = doc.RootElement.TryGetProperty("messageType", out JsonElement typeEl)
            ? typeEl.GetString() ?? ""
            : "";
        JsonElement data = doc.RootElement.TryGetProperty("data", out JsonElement dataEl)
            ? dataEl
            : default;
        if (messageType == "APIError")
        {
            string message =
                data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("message", out JsonElement messageEl)
                    ? messageEl.GetString() ?? "VTS rejected the request."
                    : "VTS rejected the request.";
            return Result.Failure<string>(message, "VTS_ERROR");
        }
        return Result.Success(data.ValueKind == JsonValueKind.Undefined ? "{}" : data.GetRawText());
    }

    private async Task<Result<JsonDocument>> RoundTripAsync(
        Guid broadcasterId,
        VtsSession session,
        string requestType,
        string? dataJson,
        CancellationToken ct
    )
    {
        string requestId = Guid.CreateVersion7().ToString();
        string frame = BuildFrame(requestType, requestId, dataJson);

        TaskCompletionSource<JsonDocument> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        session.Pending[requestId] = tcs;
        try
        {
            await session.Socket.SendAsync(frame, ct);
            JsonDocument reply = await tcs.Task.WaitAsync(RequestTimeout, _clock, ct);
            return Result.Success(reply);
        }
        catch (TimeoutException)
        {
            return Result.Failure<JsonDocument>(
                "VTube Studio did not answer within the request timeout.",
                "VTS_TIMEOUT"
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await DropSessionAsync(broadcasterId, ex.Message);
            return Result.Failure<JsonDocument>(
                "The VTS connection dropped mid-request.",
                "VTS_NOT_CONNECTED"
            );
        }
        finally
        {
            session.Pending.TryRemove(requestId, out _);
        }
    }

    /// <summary>The VTS API envelope every message rides in.</summary>
    public static string BuildFrame(string requestType, string requestId, string? dataJson)
    {
        string data = string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson;
        using JsonDocument payload = JsonDocument.Parse(data);
        return JsonSerializer.Serialize(
            new
            {
                apiName = "VTubeStudioPublicAPI",
                apiVersion = "1.0",
                requestID = requestId,
                messageType = requestType,
                data = payload.RootElement.Clone(),
            },
            WireJson
        );
    }

    private async Task<Result<VtsSession>> GetOrConnectAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        if (_sessions.TryGetValue(broadcasterId, out VtsSession? live))
            return Result.Success(live);

        SemaphoreSlim gate = _connectGates.GetOrAdd(broadcasterId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_sessions.TryGetValue(broadcasterId, out VtsSession? raced))
                return Result.Success(raced);

            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            VtsConnection? config = await db.VtsConnections.FirstOrDefaultAsync(
                c => c.BroadcasterId == broadcasterId,
                ct
            );
            if (config is null || !config.IsEnabled)
                return Result.Failure<VtsSession>(
                    "VTube Studio control is not enabled for this channel.",
                    "VTS_DISABLED"
                );
            if (config.Mode != "direct")
                return Result.Failure<VtsSession>(
                    "This channel's VTS connection runs in bridge mode.",
                    "VTS_WRONG_MODE"
                );

            string? pluginToken = await scope
                .ServiceProvider.GetRequiredService<IVtsConnectionService>()
                .GetPluginTokenForTransportAsync(broadcasterId, ct);
            if (pluginToken is null)
                return Result.Failure<VtsSession>(
                    "VTube Studio has not authorized this plugin yet — run the authorize flow.",
                    "VTS_UNAUTHORIZED"
                );

            try
            {
                Result<VtsSession> connected = await ConnectAuthenticateSubscribeAsync(
                    broadcasterId,
                    config,
                    pluginToken,
                    ct
                );
                if (connected.IsFailure)
                {
                    config.Status = "error";
                    await db.SaveChangesAsync(CancellationToken.None);
                    return connected;
                }
                _sessions[broadcasterId] = connected.Value;
                config.Status = "connected";
                config.LastConnectedAt = _clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
                return connected;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "VTS direct connect failed for channel {Channel}.",
                    broadcasterId
                );
                config.Status = "error";
                await db.SaveChangesAsync(CancellationToken.None);
                return Result.Failure<VtsSession>(
                    "Could not connect to VTube Studio at the configured endpoint.",
                    "VTS_NOT_CONNECTED"
                );
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Result<VtsSession>> ConnectAuthenticateSubscribeAsync(
        Guid broadcasterId,
        VtsConnection config,
        string pluginToken,
        CancellationToken ct
    )
    {
        IObsSocket socket = await _socketFactory.ConnectAsync(new Uri(config.Endpoint), ct);
        VtsSession session = new() { Socket = socket, ReceiveLoop = Task.CompletedTask };
        session.ReceiveLoop = Task.Run(
            () => ReceiveLoopAsync(broadcasterId, session),
            CancellationToken.None
        );

        // Session auth FIRST (D2): the stored token replays before any control request.
        string authData = JsonSerializer.Serialize(
            new
            {
                pluginName = PluginName,
                pluginDeveloper = PluginDeveloper,
                authenticationToken = pluginToken,
            },
            WireJson
        );
        Result<JsonDocument> authReply = await RoundTripAsync(
            broadcasterId,
            session,
            "AuthenticationRequest",
            authData,
            ct
        );
        if (authReply.IsFailure)
        {
            await session.DisposeAsync();
            return Result.Failure<VtsSession>(authReply.ErrorMessage!, authReply.ErrorCode!);
        }
        using JsonDocument authDoc = authReply.Value;
        bool authenticated =
            authDoc.RootElement.TryGetProperty("data", out JsonElement authDataEl)
            && authDataEl.TryGetProperty("authenticated", out JsonElement authenticatedEl)
            && authenticatedEl.GetBoolean();
        if (!authenticated)
        {
            await session.DisposeAsync();
            return Result.Failure<VtsSession>(
                "VTube Studio rejected the stored plugin token — re-run the authorize flow.",
                "VTS_UNAUTHORIZED"
            );
        }

        // Subscribe the masked event set; a subscription failure downgrades events, never control.
        foreach (string eventName in MaskedEvents(config.EventSubscriptionsMask))
        {
            string subscribeData = JsonSerializer.Serialize(
                new { eventName, subscribe = true },
                WireJson
            );
            Result<JsonDocument> subscribed = await RoundTripAsync(
                broadcasterId,
                session,
                "EventSubscriptionRequest",
                subscribeData,
                ct
            );
            if (subscribed.IsSuccess)
                subscribed.Value.Dispose();
        }

        return Result.Success(session);
    }

    public static IEnumerable<string> MaskedEvents(int mask) =>
        EventBits.Where((_, index) => (mask & (1 << index)) != 0);

    private async Task ReceiveLoopAsync(Guid broadcasterId, VtsSession session)
    {
        try
        {
            string? frame;
            while (
                (frame = await session.Socket.ReceiveTextAsync(CancellationToken.None)) is not null
            )
            {
                JsonDocument doc = JsonDocument.Parse(frame);
                string messageType = doc.RootElement.TryGetProperty(
                    "messageType",
                    out JsonElement typeEl
                )
                    ? typeEl.GetString() ?? ""
                    : "";
                string? requestId = doc.RootElement.TryGetProperty(
                    "requestID",
                    out JsonElement idEl
                )
                    ? idEl.GetString()
                    : null;

                if (
                    requestId is not null
                    && session.Pending.TryGetValue(
                        requestId,
                        out TaskCompletionSource<JsonDocument>? tcs
                    )
                )
                {
                    if (!tcs.TrySetResult(doc))
                        doc.Dispose();
                    continue;
                }

                if (messageType.EndsWith("Event", StringComparison.Ordinal))
                {
                    string payload = doc.RootElement.TryGetProperty("data", out JsonElement dataEl)
                        ? dataEl.GetRawText()
                        : "{}";
                    await _eventBus.PublishAsync(
                        new VtsEventReceived
                        {
                            BroadcasterId = broadcasterId,
                            OccurredAt = _clock.GetUtcNow(),
                            EventType = messageType,
                            PayloadJson = payload,
                        }
                    );
                }
                doc.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "VTS receive loop faulted for channel {Channel}.",
                broadcasterId
            );
        }
        finally
        {
            await DropSessionAsync(broadcasterId, "connection closed");
        }
    }

    private async Task DropSessionAsync(Guid broadcasterId, string reason)
    {
        if (!_sessions.TryRemove(broadcasterId, out VtsSession? session))
            return;
        foreach (TaskCompletionSource<JsonDocument> pending in session.Pending.Values)
            pending.TrySetException(new InvalidOperationException("VTS connection dropped."));
        session.Pending.Clear();
        await session.DisposeAsync();
        _logger.LogInformation(
            "VTS direct connection for {Channel} dropped: {Reason}.",
            broadcasterId,
            reason
        );
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Guid broadcasterId in _sessions.Keys.ToList())
            await DropSessionAsync(broadcasterId, "shutdown");
    }

    /// <summary>Sync bridge for containers disposed synchronously (tests); production uses DisposeAsync.</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
