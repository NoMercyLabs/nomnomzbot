// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Reads a channel's subscribers (Get Broadcaster Subscriptions) and VIPs (Get VIPs) from Twitch and projects them
/// to a Plane-A standing snapshot, resolving each Twitch user to a local <c>User</c> via get-or-create and folding
/// the two signals to the higher standing (Vip outranks Subscriber). Reports whether each read was COMPLETE — every
/// page read AND every member resolved — so the reconcile downgrades only against a complete picture
/// (roles-permissions §3.5). The Plane-A sibling of <see cref="TwitchManagementSnapshotBuilder"/>; shared by the
/// onboarding seed's intent and the periodic reconcile so their sourcing never drifts.
/// </summary>
public sealed class TwitchStandingSnapshotBuilder(
    IUserService users,
    ITwitchSubscriptionsApi subscriptions,
    ITwitchModeratorsApi moderators,
    ILogger<TwitchStandingSnapshotBuilder> logger
) : ITwitchStandingSnapshotBuilder
{
    public async Task<CommunityStandingSnapshot> BuildAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        // A member that resolves to both a subscriber and a VIP is recorded once at the higher (Vip) standing —
        // the map is keyed by user, and the VIP pass overwrites the subscriber entry.
        Dictionary<Guid, TwitchStandingMember> byUser = [];

        // ── Subscribers (paged) ───────────────────────────────────────────────
        // "Complete" (safe to drive a lapse downgrade) requires every page read AND every member resolved: a failed
        // page, a channel with more than one page of subscribers, or a transient get-or-create miss leaves the
        // snapshot partial, so it must not prune — better a stale Subscriber one extra cycle than a wrong downgrade.
        bool subsComplete = true;
        string? cursor = null;
        int pageGuard = 0;
        do
        {
            Result<TwitchPage<TwitchBroadcasterSubscription>> page =
                await subscriptions.GetBroadcasterSubscriptionsAsync(
                    broadcasterId,
                    filterTwitchUserIds: null,
                    new TwitchPageRequest(After: cursor),
                    ct
                );
            if (page.IsFailure)
            {
                logger.LogWarning(
                    "Standing snapshot: reading subscribers for {BroadcasterId} failed: {Error} ({Code}) — subscriber standings left intact (not pruned this run)",
                    broadcasterId,
                    page.ErrorMessage,
                    page.ErrorCode
                );
                subsComplete = false;
                break;
            }

            foreach (TwitchBroadcasterSubscription sub in page.Value.Items)
            {
                // Gift senders appear in the list but are not themselves subscribers.
                if (string.IsNullOrEmpty(sub.UserId))
                    continue;

                Guid? userId = await ResolveUserIdAsync(
                    sub.UserId,
                    sub.UserLogin,
                    sub.UserName ?? sub.UserLogin,
                    ct
                );
                if (userId is Guid id)
                    byUser[id] = new TwitchStandingMember(
                        id,
                        CommunityStanding.Subscriber,
                        sub.Tier
                    );
                else
                    subsComplete = false;
            }

            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor) && ++pageGuard < 100);

        // ── VIPs (paged) — VIP outranks Subscriber, so overwrite ──────────────
        bool vipsComplete = true;
        cursor = null;
        pageGuard = 0;
        do
        {
            Result<TwitchPage<TwitchVip>> page = await moderators.GetVipsAsync(
                broadcasterId,
                new TwitchPageRequest(After: cursor),
                ct
            );
            if (page.IsFailure)
            {
                logger.LogWarning(
                    "Standing snapshot: reading VIPs for {BroadcasterId} failed: {Error} ({Code}) — VIP standings left intact (not pruned this run)",
                    broadcasterId,
                    page.ErrorMessage,
                    page.ErrorCode
                );
                vipsComplete = false;
                break;
            }

            foreach (TwitchVip vip in page.Value.Items)
            {
                Guid? userId = await ResolveUserIdAsync(
                    vip.UserId,
                    vip.UserLogin,
                    vip.UserName ?? vip.UserLogin,
                    ct
                );
                if (userId is Guid id)
                    byUser[id] = new TwitchStandingMember(id, CommunityStanding.Vip, SubTier: null);
                else
                    vipsComplete = false;
            }

            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor) && ++pageGuard < 100);

        return new CommunityStandingSnapshot([.. byUser.Values], subsComplete, vipsComplete);
    }

    private async Task<Guid?> ResolveUserIdAsync(
        string twitchUserId,
        string username,
        string displayName,
        CancellationToken ct
    )
    {
        Result<UserDto> user = await users.GetOrCreateAsync(
            twitchUserId,
            username,
            displayName,
            cancellationToken: ct
        );
        if (user.IsFailure)
        {
            logger.LogWarning(
                "Standing snapshot: could not resolve Twitch user {TwitchUserId}: {Error}",
                twitchUserId,
                user.ErrorMessage
            );
            return null;
        }

        return Guid.TryParse(user.Value.Id, out Guid id) ? id : null;
    }
}
