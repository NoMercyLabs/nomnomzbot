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
    // "FollowEvent" is the live EventSub translation; "NewFollowerEvent" only exists in journals written by
    // legacy imports before the follow event was canonicalized — both must fold or a rebuild undercounts.
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
        "FollowEvent",
        "NewFollowerEvent",
        "RewardRedeemedEvent",
        "NewSubscriptionEvent",
        "ResubscriptionEvent",
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
        (string Provider, string ExternalUserId, string Login, string Display)? identity =
            ViewerResolver.ParseIdentity(payload);
        if (identity is null)
            return Result.Success();

        ViewerProfile? profile = await resolver.ResolveAsync(
            broadcasterId,
            identity.Value.Provider,
            identity.Value.ExternalUserId,
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

        // Fold the aggregate this event type contributes to the per-viewer profile.
        switch (@event.EventType)
        {
            case "ChatMessageReceivedEvent":
                profile.TotalMessages++;
                profile.IsSubscriber = payload["IsSubscriber"]?.Value<bool?>() ?? false;
                break;
            case "FollowEvent":
            case "NewFollowerEvent":
                profile.IsFollower = true;
                break;
            case "RewardRedeemedEvent":
                profile.TotalRedemptions++;
                break;
            case "NewSubscriptionEvent":
            case "ResubscriptionEvent":
                profile.IsSubscriber = true;
                profile.SubTier = payload["Tier"]?.Value<string>() ?? profile.SubTier;
                break;
        }

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
        // TotalWatchSeconds is deliberately NOT reset here: it is owned end to end by WatchSessionProjection (folded
        // there, zeroed by its ResetAsync), so a rebuild of either projection stays consistent.
        foreach (ViewerProfile profile in rows)
        {
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
