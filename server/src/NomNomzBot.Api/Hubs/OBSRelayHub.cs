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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Obs.Entities;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Obs.Bridge;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// The OBS bridge relay (obs-control.md §4/§7): the browser-source bridge INSIDE OBS connects here
/// with the channel's <c>BridgeToken</c> (query <c>?token=</c> — the token IS the credential; this
/// hub never accepts a dashboard JWT), registers into the per-channel election, receives
/// <c>ExecuteObsRequest</c> pushes when it is the leader, acks each command by id, and forwards the
/// OBS events it sees as <see cref="ObsEventReceivedEvent"/> for the trigger surface.
/// </summary>
[AllowAnonymous]
public class OBSRelayHub : Hub<IOBSRelayClient>
{
    private static readonly ConcurrentDictionary<string, Guid> ConnectionChannels = new();

    private readonly IApplicationDbContext _db;
    private readonly IObsBridgeRegistry _bridges;
    private readonly ObsBridgeCommandBook _commands;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;
    private readonly ILogger<OBSRelayHub> _logger;

    public OBSRelayHub(
        IApplicationDbContext db,
        IObsBridgeRegistry bridges,
        ObsBridgeCommandBook commands,
        IEventBus eventBus,
        TimeProvider clock,
        ILogger<OBSRelayHub> logger
    )
    {
        _db = db;
        _bridges = bridges;
        _commands = commands;
        _eventBus = eventBus;
        _clock = clock;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        string? token = Context.GetHttpContext()?.Request.Query["token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            Context.Abort();
            return;
        }

        // The bridge token IS the tenant selector — the lookup ignores the (unset) tenant filter.
        ObsConnection? connection = await _db
            .ObsConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.BridgeToken == token && c.DeletedAt == null && c.IsEnabled);
        if (connection is null)
        {
            _logger.LogWarning("OBS bridge rejected: unknown or disabled bridge token.");
            Context.Abort();
            return;
        }

        ConnectionChannels[Context.ConnectionId] = connection.BroadcasterId;
        await _bridges.RegisterAsync(
            connection.BroadcasterId,
            Context.ConnectionId,
            _clock.GetUtcNow().UtcDateTime,
            Context.ConnectionAborted
        );
        _logger.LogInformation(
            "OBS bridge connected for {Channel} ({Connection}).",
            connection.BroadcasterId,
            Context.ConnectionId
        );
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionChannels.TryRemove(Context.ConnectionId, out Guid broadcasterId))
        {
            await _bridges.UnregisterAsync(broadcasterId, Context.ConnectionId);
            _logger.LogInformation(
                "OBS bridge disconnected for {Channel} ({Connection}).",
                broadcasterId,
                Context.ConnectionId
            );
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>The bridge answers one pushed command: same id, the OBS outcome as JSON.</summary>
    public Task AckCommand(Guid commandId, bool ok, string? responseDataJson, string? error)
    {
        if (!ConnectionChannels.ContainsKey(Context.ConnectionId))
            return Task.CompletedTask; // never authenticated — ignore

        Dictionary<string, object?>? data = null;
        if (!string.IsNullOrWhiteSpace(responseDataJson))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseDataJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    data = new Dictionary<string, object?>();
                    foreach (JsonProperty property in doc.RootElement.EnumerateObject())
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
            }
            catch (JsonException)
            {
                // A malformed ack still settles the command as-is.
            }
        }
        _commands.Complete(commandId, new ObsResponse(ok, data, error));
        return Task.CompletedTask;
    }

    /// <summary>The LEADER bridge forwards each OBS event it sees; non-leaders stay quiet client-side.</summary>
    public async Task ForwardObsEvent(string eventType, string? eventDataJson)
    {
        if (!ConnectionChannels.TryGetValue(Context.ConnectionId, out Guid broadcasterId))
            return; // never authenticated — ignore
        if (string.IsNullOrWhiteSpace(eventType))
            return;

        await _eventBus.PublishAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = broadcasterId,
                OccurredAt = _clock.GetUtcNow(),
                ObsEventType = eventType,
                DataJson = eventDataJson ?? "{}",
            }
        );
    }
}
