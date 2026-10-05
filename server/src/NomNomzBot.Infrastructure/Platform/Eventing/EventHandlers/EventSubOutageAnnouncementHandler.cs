// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;

/// <summary>
/// Tells chat when the Twitch event connection drops and when it is back, once per outage, like the old bot
/// (events — redeems, commands — are lost while it is down; chat sends go over Helix REST and still work).
/// <para>
/// Only an UNPLANNED drop publishes <see cref="EventSubDisconnectedEvent"/>: the transport's
/// <c>session_reconnect</c> handoff never does, so the ~5-minute planned reconnect posts nothing. A retry
/// storm publishes the event again for every failed attempt; <see cref="EventSubOutageLedger"/> collapses
/// those into one line. The shared bot session (<c>Guid.Empty</c>) carries every channel, so it speaks in
/// every live channel; a broadcaster's own session speaks only in that channel. The recovery line goes only
/// to the channels that heard the drop. Lines come from the channel's tone like every other bot status line.
/// </para>
/// </summary>
public sealed class EventSubOutageAnnouncementHandler(
    EventSubOutageLedger ledger,
    IApplicationDbContext db,
    IBuiltinResponseComposer composer,
    IChatProvider chat,
    ILogger<EventSubOutageAnnouncementHandler> logger
) : IEventHandler<EventSubDisconnectedEvent>, IEventHandler<EventSubConnectedEvent>
{
    public async Task HandleAsync(
        EventSubDisconnectedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (!ledger.TryBegin(@event.BroadcasterId))
            return;

        List<(Guid Id, string Personality)> channels = await LoadLiveChannelsAsync(
            @event.BroadcasterId,
            cancellationToken
        );
        List<Guid> told = [];
        foreach ((Guid id, string personality) in channels)
        {
            if (
                await SendAsync(
                    id,
                    personality,
                    BuiltinResponseSlots.BotStatus.ConnectionLost,
                    cancellationToken
                )
            )
                told.Add(id);
        }

        ledger.RecordAnnounced(@event.BroadcasterId, told);
    }

    public async Task HandleAsync(
        EventSubConnectedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<Guid> told = ledger.End(@event.BroadcasterId);
        if (told.Count == 0)
            return;

        var rows = await db
            .Channels.AsNoTracking()
            .Where(c => told.Contains(c.Id))
            .Select(c => new { c.Id, c.Personality })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            await SendAsync(
                row.Id,
                row.Personality,
                BuiltinResponseSlots.BotStatus.ConnectionRestored,
                cancellationToken
            );
        }
    }

    /// <summary>Channels the drop concerns that are live right now — nobody reads an offline chat.</summary>
    private async Task<List<(Guid Id, string Personality)>> LoadLiveChannelsAsync(
        Guid ownerId,
        CancellationToken ct
    )
    {
        var rows = await db
            .Channels.AsNoTracking()
            .Where(c =>
                (ownerId == Guid.Empty || c.Id == ownerId)
                && c.Enabled
                && c.IsOnboarded
                && c.IsLive
                && c.Status == AuthEnums.ChannelStatus.Active
            )
            .Select(c => new { c.Id, c.Personality })
            .ToListAsync(ct);

        return [.. rows.Select(r => (r.Id, r.Personality))];
    }

    private async Task<bool> SendAsync(
        Guid broadcasterId,
        string personality,
        string slot,
        CancellationToken ct
    )
    {
        try
        {
            string message = await composer.ComposeAsync(
                new BuiltinResponseRequest
                {
                    BroadcasterId = broadcasterId,
                    Personality = personality,
                    BuiltinKey = BuiltinResponseSlots.BotStatus.Key,
                    Slot = slot,
                    NeutralFallback =
                        ToneTemplateCatalog.ShippedTemplate(
                            BuiltinResponseSlots.BotStatus.Key,
                            slot
                        ) ?? string.Empty,
                },
                ct
            );
            if (message.Length == 0)
                return false;

            return await chat.SendMessageAsync(broadcasterId, message, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Outage chat line ({Slot}) failed for channel {BroadcasterId}.",
                slot,
                broadcasterId
            );
            return false;
        }
    }
}
