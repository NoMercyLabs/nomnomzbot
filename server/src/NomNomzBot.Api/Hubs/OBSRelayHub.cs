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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Hubs;

[Authorize]
public class OBSRelayHub : Hub<IOBSRelayClient>
{
    private static readonly ConcurrentDictionary<string, string> _connectionBroadcaster = new();
    private readonly ILogger<OBSRelayHub> _logger;
    private readonly IChannelAccessService _access;

    public OBSRelayHub(ILogger<OBSRelayHub> logger, IChannelAccessService access)
    {
        _logger = logger;
        _access = access;
    }

    public override async Task OnConnectedAsync()
    {
        string? userId = Context.UserIdentifier ?? Context.User?.FindFirst("sub")?.Value;
        if (userId == null)
        {
            Context.Abort();
            return;
        }

        Guid broadcasterId = await _access.ResolveOwnChannelAsync(userId);
        if (broadcasterId == Guid.Empty)
        {
            Context.Abort();
            return;
        }

        _connectionBroadcaster[Context.ConnectionId] = broadcasterId.ToString();
        await Groups.AddToGroupAsync(Context.ConnectionId, $"obs-{broadcasterId}");
        _logger.LogDebug("OBSRelay connected for {BroadcasterId}", broadcasterId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connectionBroadcaster.TryRemove(Context.ConnectionId, out string? broadcasterId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"obs-{broadcasterId}");
            // Implicitly fire OBSDisconnected event
            await Clients.Group($"obs-{broadcasterId}").OBSCommand(new("", "disconnected", null));
        }
        await base.OnDisconnectedAsync(exception);
    }

    public Task OBSResponse(OBSResponseDto response)
    {
        _logger.LogDebug("OBS response for request {R}: {S}", response.RequestId, response.Success);
        return Task.CompletedTask;
    }

    public Task OBSStateUpdate(OBSStateUpdateDto update)
    {
        _logger.LogDebug("OBS state update: {S}", update.State);
        return Task.CompletedTask;
    }

    public async Task OBSConnected(OBSConnectedDto dto)
    {
        _logger.LogInformation(
            "OBS WebSocket connected for {B}, version {V}",
            dto.BroadcasterId,
            dto.Version
        );
    }

    public async Task OBSDisconnected()
    {
        _logger.LogInformation(
            "OBS WebSocket disconnected for connection {C}",
            Context.ConnectionId
        );
    }
}
