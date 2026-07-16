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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Derives per-stream watch sessions from demonstrated presence (analytics.md §1.1, schema M.2). Twitch exposes no
/// per-viewer presence stream, so a session is derived: on a viewer's activity (chat/command/redemption) inside a
/// live window it opens (or extends) a session keyed by (channel, viewer, stream). The live window is resolved from
/// the durable stream history via <see cref="ILiveWindowResolver"/> (historically accurate on replay).
/// <see cref="WatchSession.PresenceConfirmed"/> flips once an activity lands ≥60s after the session start — the
/// anti-AFK basis the economy's watch-time earning consumes. Plain rollup — rebuild = remove + refold.
/// </summary>
public sealed class WatchSessionProjection(
    IApplicationDbContext db,
    ViewerResolver viewerResolver,
    ILiveWindowResolver liveWindow
) : IProjection
{
    private const int PresenceThresholdSeconds = 60;

    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
        "CommandExecutedEvent",
        "RewardRedeemedEvent",
    };

    public string Name => "watch-session";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId is not Guid broadcasterId)
            return Result.Success();

        // Presence is only counted inside a live window — no covering stream, no session (§1.1).
        string? streamId = await liveWindow.GetCoveringStreamIdAsync(
            broadcasterId,
            @event.OccurredAt,
            cancellationToken
        );
        if (streamId is null)
            return Result.Success();

        (string Provider, string ExternalUserId, string Login, string Display)? identity =
            ViewerResolver.ParseIdentity(@event.PayloadJson);
        if (identity is null)
            return Result.Success();

        ViewerProfile? profile = await viewerResolver.ResolveAsync(
            broadcasterId,
            identity.Value.Provider,
            identity.Value.ExternalUserId,
            identity.Value.Login,
            identity.Value.Display,
            cancellationToken
        );
        if (profile is null)
            return Result.Success();

        WatchSession session = await GetOrOpenAsync(
            broadcasterId,
            profile,
            streamId,
            @event.OccurredAt,
            cancellationToken
        );

        session.EndedAt = @event.OccurredAt;
        session.DurationSeconds = (long)(@event.OccurredAt - session.StartedAt).TotalSeconds;
        if (@event.EventType == "ChatMessageReceivedEvent")
            session.MessageCountInSession++;
        if (
            !session.PresenceConfirmed
            && (@event.OccurredAt - session.StartedAt).TotalSeconds >= PresenceThresholdSeconds
        )
            session.PresenceConfirmed = true;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<WatchSession> rows = await (
            broadcasterId is Guid id
                ? db.WatchSessions.Where(s => s.BroadcasterId == id)
                : db.WatchSessions
        ).ToListAsync(cancellationToken);
        db.WatchSessions.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<WatchSession> GetOrOpenAsync(
        Guid broadcasterId,
        ViewerProfile profile,
        string streamId,
        DateTime at,
        CancellationToken ct
    )
    {
        // IgnoreQueryFilters: tenant-less projection-driver / rebuild scope — the ITenantScoped filter would hide
        // the open session just committed for this (broadcaster, viewer, stream), causing a re-insert + 23505.
        WatchSession? session = await db
            .WatchSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                s =>
                    s.BroadcasterId == broadcasterId
                    && s.ViewerUserId == profile.ViewerUserId
                    && s.StreamId == streamId,
                ct
            );
        if (session is null)
        {
            session = new WatchSession
            {
                BroadcasterId = broadcasterId,
                ViewerProfileId = profile.Id,
                ViewerUserId = profile.ViewerUserId,
                StreamId = streamId,
                StartedAt = at,
                EndedAt = at,
                CreatedAt = at,
            };
            db.WatchSessions.Add(session);
        }
        return session;
    }
}
