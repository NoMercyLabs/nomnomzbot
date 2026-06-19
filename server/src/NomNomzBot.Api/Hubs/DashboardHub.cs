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
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs;

[Authorize]
public class DashboardHub : Hub<IDashboardClient>
{
    private static readonly ConcurrentDictionary<string, string> _connectionChannel = new(); // connectionId -> broadcasterId
    private readonly IChannelRegistry _registry;
    private readonly ILogger<DashboardHub> _logger;
    private readonly IChatProvider _chat;

    public DashboardHub(IChannelRegistry registry, ILogger<DashboardHub> logger, IChatProvider chat)
    {
        _registry = registry;
        _logger = logger;
        _chat = chat;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Dashboard connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connectionChannel.TryRemove(Context.ConnectionId, out string? broadcasterId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"channel-{broadcasterId}");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<JoinChannelResponse> JoinChannel(string broadcasterId)
    {
        ChannelContext? ctx = _registry.Get(broadcasterId);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"channel-{broadcasterId}");
        _connectionChannel[Context.ConnectionId] = broadcasterId;
        _logger.LogDebug("Connection {C} joined channel {B}", Context.ConnectionId, broadcasterId);

        StreamStatusDto status =
            ctx != null
                ? new(
                    ctx.IsLive,
                    ctx.CurrentStreamId,
                    ctx.CurrentTitle,
                    ctx.CurrentGame,
                    ctx.WentLiveAt?.ToString("O")
                )
                : new StreamStatusDto(false, null, null, null, null);

        return new(true, null, status);
    }

    public async Task LeaveChannel(string broadcasterId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"channel-{broadcasterId}");
        _connectionChannel.TryRemove(Context.ConnectionId, out _);
    }

    public async Task<SendMessageResponse> SendChatMessage(string broadcasterId, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 500)
            return new(false, "Message too long or empty", null);

        try
        {
            await _chat.SendMessageAsync(broadcasterId, message);
            return new(true, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat message for {B}", broadcasterId);
            return new(false, "Failed to send message", null);
        }
    }

    public async Task<ActionResponse> TriggerAction(
        string broadcasterId,
        string action,
        object? data
    )
    {
        _logger.LogInformation("TriggerAction {Action} for {B}", action, broadcasterId);
        // Action routing handled by business layer; return placeholder
        return new(true, null);
    }
}
