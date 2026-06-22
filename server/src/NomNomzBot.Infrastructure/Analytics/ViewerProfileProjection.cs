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
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Folds the journal into the per-viewer-per-channel profile (analytics.md §3.1, schema M.1 — the anonymization
/// anchor). Resolves the viewer-as-User + the profile anchor via the shared <see cref="ViewerResolver"/>
/// ([[viewer-identity-is-user]]), then folds the aggregates it owns. PII snapshots come from the journal payload,
/// so a GDPR-scrubbed payload re-projects anonymized. Folds chat today; extends to the other activity events next.
/// </summary>
public sealed class ViewerProfileProjection(IApplicationDbContext db, ViewerResolver resolver)
    : IProjection
{
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
    };

    public string Name => "viewer-profile";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId is not Guid broadcasterId)
            return Result.Success();

        JObject? payload = ViewerResolver.TryParse(@event.PayloadJson);
        if (payload is null)
            return Result.Success();
        (string TwitchUserId, string Login, string Display)? identity =
            ViewerResolver.ParseChatIdentity(payload);
        if (identity is null)
            return Result.Success();

        ViewerProfile? profile = await resolver.ResolveAsync(
            broadcasterId,
            identity.Value.TwitchUserId,
            identity.Value.Login,
            identity.Value.Display,
            cancellationToken
        );
        if (profile is null)
            return Result.Success();

        profile.UsernameSnapshot = identity.Value.Login;
        profile.DisplayNameSnapshot = identity.Value.Display;
        profile.FirstSeenAt ??= @event.OccurredAt;
        profile.LastSeenAt = @event.OccurredAt;
        profile.TotalMessages++;
        profile.IsSubscriber = payload["IsSubscriber"]?.Value<bool?>() ?? false;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ViewerProfile> rows = await (
            broadcasterId is Guid id
                ? db.ViewerProfiles.Where(p => p.BroadcasterId == id)
                : db.ViewerProfiles
        ).ToListAsync(cancellationToken);

        // Zero the folded aggregates in place (the row is the soft-delete anchor — never hard-removed on rebuild).
        foreach (ViewerProfile profile in rows)
        {
            profile.TotalWatchSeconds = 0;
            profile.TotalMessages = 0;
            profile.TotalCommandsUsed = 0;
            profile.TotalRedemptions = 0;
            profile.TotalSongRequests = 0;
            profile.FirstSeenAt = null;
            profile.LastSeenAt = null;
            profile.IsFollower = false;
            profile.IsSubscriber = false;
            profile.SubTier = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
