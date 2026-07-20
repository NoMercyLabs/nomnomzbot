// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// Builds a channel's Plane-A community-standing snapshot from Twitch (roles-permissions §3.5) — its subscribers
/// (Get Broadcaster Subscriptions) and VIPs (Get VIPs) — resolving each Twitch user to a local <c>User</c> Guid and
/// folding the two signals to the higher standing (Vip outranks Subscriber). The sibling of
/// <see cref="ITwitchManagementSnapshotBuilder"/> for Plane A: it reports whether the subscriber and VIP reads were
/// each COMPLETE (every page read, every member resolved), so the reconcile downgrades a standing only when it has a
/// complete picture and never on a partial/failed read.
/// </summary>
public interface ITwitchStandingSnapshotBuilder
{
    Task<CommunityStandingSnapshot> BuildAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <summary>
/// A freshly-read Twitch community-standing snapshot: each resolved viewer's highest Twitch standing (Vip or
/// Subscriber, with the sub tier when subscribed), plus whether the subscriber and VIP reads were each COMPLETE.
/// A lapse downgrade is applied only when BOTH are authoritative (the full picture); a partial read raises only.
/// </summary>
public sealed record CommunityStandingSnapshot(
    IReadOnlyList<TwitchStandingMember> Members,
    bool SubscribersAuthoritative,
    bool VipsAuthoritative
);

/// <summary>One resolved viewer's Twitch-sourced community standing (the higher of their sub / VIP signal).</summary>
public sealed record TwitchStandingMember(Guid UserId, CommunityStanding Standing, string? SubTier);
