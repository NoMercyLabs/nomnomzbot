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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs;

[Authorize]
public class DashboardHub : Hub<IDashboardClient>
{
    // connectionId -> channelId -> the event CLASSES this connection subscribed for that channel. A dashboard
    // connection may watch MANY channels at once (chat-client multi-watch: a moderator monitoring several
    // channels' chat/events in one session), each with its own class set (BUILD item 5 — a chat-only pane
    // need not receive live-ops/music pushes). SignalR groups fan the pushes; this map is the authoritative
    // per-connection record of exactly which groups were joined, so LeaveChannel drops one channel's base +
    // class groups and disconnect cleans up everything (not just the most recent join).
    private static readonly ConcurrentDictionary<
        string,
        ConcurrentDictionary<string, IReadOnlyList<string>>
    > _connectionChannels = new();
    private readonly IChannelRegistry _registry;
    private readonly ILogger<DashboardHub> _logger;
    private readonly IChatProvider _chat;
    private readonly IChannelAccessService _access;
    private readonly IActionAuthorizationService _authorization;
    private readonly IOperatorChatSender _operatorSender;

    public DashboardHub(
        IChannelRegistry registry,
        ILogger<DashboardHub> logger,
        IChatProvider chat,
        IChannelAccessService access,
        IActionAuthorizationService authorization,
        IOperatorChatSender operatorSender
    )
    {
        _registry = registry;
        _logger = logger;
        _chat = chat;
        _access = access;
        _authorization = authorization;
        _operatorSender = operatorSender;
    }

    private string? CallerId => Context.UserIdentifier ?? Context.User?.FindFirst("sub")?.Value;

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("Dashboard connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (
            _connectionChannels.TryRemove(
                Context.ConnectionId,
                out ConcurrentDictionary<string, IReadOnlyList<string>>? channels
            )
        )
        {
            foreach ((string broadcasterId, IReadOnlyList<string> classes) in channels)
                await RemoveFromChannelGroupsAsync(broadcasterId, classes);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Joins a channel with EVERY push class — the universal dashboard shell wants everything.</summary>
    public Task<JoinChannelResponse> JoinChannel(string broadcasterId) =>
        JoinChannelCoreAsync(broadcasterId, DashboardEventClasses.All);

    /// <summary>
    /// Joins a channel subscribing only the given push classes (BUILD item 5 — e.g. a chat-only pane passes
    /// <c>["chat"]</c> and never receives live-ops/music traffic). Core pushes (stream status, config /
    /// permission / reward invalidations, alerts) are always on. Re-joining the same channel REPLACES its
    /// class set. Unknown class keys fail honestly rather than silently subscribing to nothing.
    /// </summary>
    public async Task<JoinChannelResponse> JoinChannelClasses(
        string broadcasterId,
        string[] eventClasses
    )
    {
        string[] invalid = [.. eventClasses.Where(c => !DashboardEventClasses.IsValid(c))];
        if (invalid.Length > 0)
            return new(
                false,
                $"Unknown event class(es): {string.Join(", ", invalid)}. "
                    + $"Valid: {string.Join(", ", DashboardEventClasses.All)}.",
                null
            );

        return await JoinChannelCoreAsync(
            broadcasterId,
            eventClasses.Distinct(StringComparer.Ordinal).ToArray()
        );
    }

    private async Task<JoinChannelResponse> JoinChannelCoreAsync(
        string broadcasterId,
        IReadOnlyList<string> eventClasses
    )
    {
        string? userId = CallerId;
        if (userId == null)
            return new(false, "Not authenticated", null);

        if (!await _access.CanResolveTenantAsync(userId, broadcasterId))
            return new(false, "Access denied", null);

        ChannelContext? ctx = Guid.TryParse(broadcasterId, out Guid tenantId)
            ? _registry.Get(tenantId)
            : null;

        ConcurrentDictionary<string, IReadOnlyList<string>> channels = _connectionChannels.GetOrAdd(
            Context.ConnectionId,
            static _ => new ConcurrentDictionary<string, IReadOnlyList<string>>()
        );

        // A re-join replaces the channel's class set — drop groups no longer wanted before adding.
        if (channels.TryGetValue(broadcasterId, out IReadOnlyList<string>? previous))
            foreach (string stale in previous.Except(eventClasses, StringComparer.Ordinal))
                await Groups.RemoveFromGroupAsync(
                    Context.ConnectionId,
                    DashboardEventClasses.ClassGroup(broadcasterId, stale)
                );

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            DashboardEventClasses.BaseGroup(broadcasterId)
        );
        foreach (string eventClass in eventClasses)
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                DashboardEventClasses.ClassGroup(broadcasterId, eventClass)
            );
        channels[broadcasterId] = eventClasses;

        _logger.LogDebug(
            "Connection {C} joined channel {B} (classes: {Classes})",
            Context.ConnectionId,
            broadcasterId,
            string.Join(",", eventClasses)
        );

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
        IReadOnlyList<string> joinedClasses = DashboardEventClasses.All;
        if (
            _connectionChannels.TryGetValue(
                Context.ConnectionId,
                out ConcurrentDictionary<string, IReadOnlyList<string>>? channels
            )
        )
        {
            if (channels.TryRemove(broadcasterId, out IReadOnlyList<string>? tracked))
                joinedClasses = tracked;
            if (channels.IsEmpty)
                _connectionChannels.TryRemove(Context.ConnectionId, out _);
        }

        await RemoveFromChannelGroupsAsync(broadcasterId, joinedClasses);
    }

    private async Task RemoveFromChannelGroupsAsync(
        string broadcasterId,
        IReadOnlyList<string> eventClasses
    )
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            DashboardEventClasses.BaseGroup(broadcasterId)
        );
        foreach (string eventClass in eventClasses)
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                DashboardEventClasses.ClassGroup(broadcasterId, eventClass)
            );
    }

    public async Task<SendMessageResponse> SendChatMessage(
        string broadcasterId,
        string message,
        string senderIdentity = "you"
    )
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 500)
            return new(false, "Message too long or empty", null);

        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return new(false, "Invalid channel", null);

        // Hubs cannot carry [RequireAction], so this enforces the SAME two gates the REST send path
        // (ChatController POST messages) gets from the middleware + attribute: Gate 1 entry
        // (CanResolveTenantAsync) and Gate 2 `chat:send` (Moderator floor) — sending in chat (as the operator by
        // default, or the bot) is a moderator action, never an any-authenticated-caller one.
        string? userId = CallerId;
        if (userId is null || !Guid.TryParse(userId, out Guid callerId))
            return new(false, "Not authenticated", null);

        if (!await _access.CanResolveTenantAsync(userId, broadcasterId))
            return new(false, "Access denied", null);

        Result<bool> allowed = await _authorization.AuthorizeActionAsync(
            callerId,
            tenantId,
            "chat:send"
        );
        if (!allowed.IsSuccess || !allowed.Value)
            return new(false, "Access denied", null);

        try
        {
            // Honour the real outcome — a swallowed Helix failure (dead token, no connection, or the operator is
            // banned/timed-out here) returns a failure, and reporting success would LIE to the dashboard exactly
            // like the old {data:true} path. Default identity is the operator (their own account); "bot" sends as
            // the bot account instead (chat-client.md §3.1).
            bool sent;
            string? error = null;
            if (string.Equals(senderIdentity, "bot", StringComparison.OrdinalIgnoreCase))
            {
                sent = await _chat.SendMessageAsync(tenantId, message);
            }
            else
            {
                Result operatorResult = await _operatorSender.SendAsUserAsync(
                    callerId,
                    tenantId,
                    message,
                    null
                );
                sent = operatorResult.IsSuccess;
                error = operatorResult.ErrorMessage;
            }

            return sent
                ? new SendMessageResponse(true, null, null)
                : new SendMessageResponse(
                    false,
                    error ?? "The message could not be sent to Twitch.",
                    null
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat message for {B}", broadcasterId);
            return new(false, "Failed to send message", null);
        }
    }

    public Task<ActionResponse> TriggerAction(string broadcasterId, string action, object? data)
    {
        // Not wired to any business action yet. Returning success here would LIE to the dashboard —
        // the caller would believe the action ran when nothing happened. Fail honestly instead.
        _logger.LogWarning(
            "TriggerAction {Action} for {B} rejected: not implemented",
            action,
            broadcasterId
        );
        return Task.FromResult(new ActionResponse(false, "Not implemented."));
    }
}
