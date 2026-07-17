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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Entities;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Obs.Transport;

/// <summary>
/// The self-host OBS transport (obs-control.md §3.2/D1): the server holds one OBS-WebSocket v5
/// connection per channel to the channel's OWN configured endpoint (no other host is ever dialed —
/// no SSRF surface). Connects lazily on first send: Hello (op 0) → Identify (op 1, the v5
/// challenge/salt SHA-256 handshake when a password is set, subscribing with the channel's event
/// mask) → Identified (op 2). Requests are op 6/8 with awaited correlated responses (op 7/9);
/// inbound events (op 5) publish <see cref="ObsEventReceivedEvent"/> for the trigger surface. A
/// dropped socket fails all in-flight requests, records <c>LastError</c> on the row, and the next
/// send re-dials.
/// </summary>
public sealed class DirectObsTransport : IObsTransport, IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly IObsSocketFactory _socketFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;
    private readonly ILogger<DirectObsTransport> _logger;

    private readonly ConcurrentDictionary<Guid, ObsSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _connectGates = new();

    public DirectObsTransport(
        IObsSocketFactory socketFactory,
        IServiceScopeFactory scopeFactory,
        IEventBus eventBus,
        TimeProvider clock,
        ILogger<DirectObsTransport> logger
    )
    {
        _socketFactory = socketFactory;
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _clock = clock;
        _logger = logger;
    }

    private sealed class ObsSession : IAsyncDisposable
    {
        public required IObsSocket Socket { get; init; }
        public required Task ReceiveLoop { get; set; }
        public ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> Pending { get; } =
            new();

        public ValueTask DisposeAsync() => Socket.DisposeAsync();
    }

    public async Task<Result<ObsResponse>> SendAsync(
        Guid broadcasterId,
        Guid commandId,
        ObsRequest request,
        CancellationToken ct = default
    )
    {
        Result<ObsSession> session = await GetOrConnectAsync(broadcasterId, ct);
        if (session.IsFailure)
            return Result.Failure<ObsResponse>(session.ErrorMessage!, session.ErrorCode!);

        string requestId = commandId.ToString();
        string frame = JsonSerializer.Serialize(
            new
            {
                op = 6,
                d = new
                {
                    requestType = request.RequestType,
                    requestId,
                    requestData = request.RequestData,
                },
            },
            WireJson
        );

        Result<JsonElement> reply = await RoundTripAsync(
            broadcasterId,
            session.Value,
            requestId,
            frame,
            ct
        );
        if (reply.IsFailure)
            return Result.Failure<ObsResponse>(reply.ErrorMessage!, reply.ErrorCode!);
        return Result.Success(ParseRequestResponse(reply.Value));
    }

    public async Task<Result<IReadOnlyList<ObsResponse>>> SendBatchAsync(
        Guid broadcasterId,
        Guid commandId,
        ObsRequestBatch batch,
        CancellationToken ct = default
    )
    {
        Result<ObsSession> session = await GetOrConnectAsync(broadcasterId, ct);
        if (session.IsFailure)
            return Result.Failure<IReadOnlyList<ObsResponse>>(
                session.ErrorMessage!,
                session.ErrorCode!
            );

        string requestId = commandId.ToString();
        string frame = JsonSerializer.Serialize(
            new
            {
                op = 8,
                d = new
                {
                    requestId,
                    haltOnFailure = batch.HaltOnFailure,
                    executionType = (int)batch.Execution,
                    requests = batch
                        .Requests.Select(r => new
                        {
                            requestType = r.RequestType,
                            requestData = r.RequestData,
                        })
                        .ToArray(),
                },
            },
            WireJson
        );

        Result<JsonElement> reply = await RoundTripAsync(
            broadcasterId,
            session.Value,
            requestId,
            frame,
            ct
        );
        if (reply.IsFailure)
            return Result.Failure<IReadOnlyList<ObsResponse>>(
                reply.ErrorMessage!,
                reply.ErrorCode!
            );

        List<ObsResponse> results = [];
        if (
            reply.Value.TryGetProperty("results", out JsonElement resultsEl)
            && resultsEl.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement item in resultsEl.EnumerateArray())
                results.Add(ParseRequestResponse(item));
        }
        return Result.Success<IReadOnlyList<ObsResponse>>(results);
    }

    /// <summary>Sends one frame and awaits its correlated op 7/9 reply (or times out / fails on drop).</summary>
    private async Task<Result<JsonElement>> RoundTripAsync(
        Guid broadcasterId,
        ObsSession session,
        string requestId,
        string frame,
        CancellationToken ct
    )
    {
        TaskCompletionSource<JsonElement> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        session.Pending[requestId] = tcs;
        try
        {
            await session.Socket.SendAsync(frame, ct);
            JsonElement reply = await tcs.Task.WaitAsync(RequestTimeout, _clock, ct);
            return Result.Success(reply);
        }
        catch (TimeoutException)
        {
            return Result.Failure<JsonElement>(
                "OBS did not answer within the request timeout.",
                "OBS_TIMEOUT"
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await DropSessionAsync(broadcasterId, ex.Message);
            return Result.Failure<JsonElement>(
                "The OBS connection dropped mid-request.",
                "OBS_NOT_CONNECTED"
            );
        }
        finally
        {
            session.Pending.TryRemove(requestId, out _);
        }
    }

    private static ObsResponse ParseRequestResponse(JsonElement d)
    {
        bool ok = false;
        string? error = null;
        if (d.TryGetProperty("requestStatus", out JsonElement status))
        {
            ok = status.TryGetProperty("result", out JsonElement resultEl) && resultEl.GetBoolean();
            if (!ok && status.TryGetProperty("comment", out JsonElement commentEl))
                error = commentEl.GetString();
            if (!ok && error is null && status.TryGetProperty("code", out JsonElement codeEl))
                error = $"OBS request failed (code {codeEl.GetInt32()}).";
        }

        Dictionary<string, object?>? data = null;
        if (
            d.TryGetProperty("responseData", out JsonElement dataEl)
            && dataEl.ValueKind == JsonValueKind.Object
        )
        {
            data = new Dictionary<string, object?>();
            foreach (JsonProperty property in dataEl.EnumerateObject())
                data[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText(),
                };
        }

        return new ObsResponse(ok, data, error);
    }

    private async Task<Result<ObsSession>> GetOrConnectAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        if (_sessions.TryGetValue(broadcasterId, out ObsSession? live))
            return Result.Success(live);

        SemaphoreSlim gate = _connectGates.GetOrAdd(broadcasterId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_sessions.TryGetValue(broadcasterId, out ObsSession? raced))
                return Result.Success(raced);

            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            ObsConnection? config = await db.ObsConnections.FirstOrDefaultAsync(
                c => c.BroadcasterId == broadcasterId,
                ct
            );
            if (config is null || !config.IsEnabled)
                return Result.Failure<ObsSession>(
                    "OBS control is not enabled for this channel.",
                    "OBS_DISABLED"
                );
            if (config.Mode != "direct")
                return Result.Failure<ObsSession>(
                    "This channel's OBS connection runs in bridge mode.",
                    "OBS_WRONG_MODE"
                );

            string? password = await scope
                .ServiceProvider.GetRequiredService<IObsConnectionService>()
                .GetPasswordForTransportAsync(broadcasterId, ct);

            try
            {
                ObsSession session = await ConnectAndIdentifyAsync(
                    broadcasterId,
                    config,
                    password,
                    ct
                );
                _sessions[broadcasterId] = session;
                config.LastConnectedAt = _clock.GetUtcNow().UtcDateTime;
                config.LastError = null;
                await db.SaveChangesAsync(ct);
                return Result.Success(session);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "OBS direct connect failed for channel {Channel}.",
                    broadcasterId
                );
                config.LastError = Truncate(ex.Message, 300);
                await db.SaveChangesAsync(CancellationToken.None);
                return Result.Failure<ObsSession>(
                    "Could not connect to OBS at the configured endpoint.",
                    "OBS_NOT_CONNECTED"
                );
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ObsSession> ConnectAndIdentifyAsync(
        Guid broadcasterId,
        ObsConnection config,
        string? password,
        CancellationToken ct
    )
    {
        Uri uri = new($"ws://{config.Host ?? "127.0.0.1"}:{config.Port ?? 4455}");
        IObsSocket socket = await _socketFactory.ConnectAsync(uri, ct);

        string? helloFrame = await socket.ReceiveTextAsync(ct);
        if (helloFrame is null)
            throw new InvalidOperationException("OBS closed the socket before Hello.");
        using JsonDocument hello = JsonDocument.Parse(helloFrame);
        JsonElement helloData = hello.RootElement.GetProperty("d");

        string? authentication = null;
        if (helloData.TryGetProperty("authentication", out JsonElement authEl))
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException(
                    "OBS requires a password but none is configured."
                );
            string challenge = authEl.GetProperty("challenge").GetString()!;
            string salt = authEl.GetProperty("salt").GetString()!;
            authentication = ComputeAuthentication(password, salt, challenge);
        }

        string identify = JsonSerializer.Serialize(
            new
            {
                op = 1,
                d = new
                {
                    rpcVersion = 1,
                    authentication,
                    eventSubscriptions = config.EventSubscriptionsMask,
                },
            },
            WireJson
        );
        await socket.SendAsync(identify, ct);

        string? identifiedFrame = await socket.ReceiveTextAsync(ct);
        if (identifiedFrame is null)
            throw new InvalidOperationException(
                "OBS closed the socket during Identify (wrong password?)."
            );
        using JsonDocument identified = JsonDocument.Parse(identifiedFrame);
        if (identified.RootElement.GetProperty("op").GetInt32() != 2)
            throw new InvalidOperationException("OBS rejected the Identify handshake.");

        ObsSession session = new() { Socket = socket, ReceiveLoop = Task.CompletedTask };
        session.ReceiveLoop = Task.Run(
            () => ReceiveLoopAsync(broadcasterId, session),
            CancellationToken.None
        );
        return session;
    }

    /// <summary>The v5 handshake: base64(sha256(base64(sha256(password + salt)) + challenge)) — binary SHA-256 (D7).</summary>
    public static string ComputeAuthentication(string password, string salt, string challenge)
    {
        string secret = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(password + salt))
        );
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private async Task ReceiveLoopAsync(Guid broadcasterId, ObsSession session)
    {
        try
        {
            string? frame;
            while (
                (frame = await session.Socket.ReceiveTextAsync(CancellationToken.None)) is not null
            )
            {
                using JsonDocument doc = JsonDocument.Parse(frame);
                int op = doc.RootElement.GetProperty("op").GetInt32();
                JsonElement d = doc.RootElement.GetProperty("d");
                switch (op)
                {
                    case 7 or 9:
                        string? requestId = d.TryGetProperty("requestId", out JsonElement idEl)
                            ? idEl.GetString()
                            : null;
                        if (
                            requestId is not null
                            && session.Pending.TryGetValue(
                                requestId,
                                out TaskCompletionSource<JsonElement>? tcs
                            )
                        )
                            tcs.TrySetResult(d.Clone());
                        break;
                    case 5:
                        string eventType = d.GetProperty("eventType").GetString() ?? "";
                        string dataJson = d.TryGetProperty("eventData", out JsonElement eventData)
                            ? eventData.GetRawText()
                            : "{}";
                        await _eventBus.PublishAsync(
                            new ObsEventReceivedEvent
                            {
                                BroadcasterId = broadcasterId,
                                OccurredAt = _clock.GetUtcNow(),
                                ObsEventType = eventType,
                                DataJson = dataJson,
                            }
                        );
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "OBS receive loop faulted for channel {Channel}.",
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
        if (!_sessions.TryRemove(broadcasterId, out ObsSession? session))
            return;
        foreach (TaskCompletionSource<JsonElement> pending in session.Pending.Values)
            pending.TrySetException(new InvalidOperationException("OBS connection dropped."));
        session.Pending.Clear();
        await session.DisposeAsync();
        _logger.LogInformation(
            "OBS direct connection for {Channel} dropped: {Reason}.",
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

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
