// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// The shared-chat ban trust web (moderation.md §3.5, schema J.9/J.9a) — per-channel OPT-IN policy plus the
/// directional allow-list of partner channels whose shared-chat bans this channel accepts. Everything is
/// default-deny: both switches default OFF and an empty trust list accepts nothing. Writes are SuperMod-tier
/// (Gate-2 <c>moderation:sharedban:write</c>) and the service re-verifies the floor in-process via
/// <c>IRoleResolver.ResolveEffectiveLevelAsync ≥ SuperMod(20)</c> — never trust the gate alone.
/// </summary>
public interface ISharedBanService
{
    /// <summary>The channel's policy + trust list; a channel with no row reads as the defaults (both OFF, empty list).</summary>
    Task<Result<SharedBanSettingsDto>> GetSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Upserts the policy row (both switches explicit — no partial writes).</summary>
    Task<Result<SharedBanSettingsDto>> SaveSettingsAsync(
        Guid broadcasterId,
        Guid actorUserId,
        SaveSharedBanSettingsRequest request,
        CancellationToken ct = default
    );

    /// <summary>Adds a partner to the trust list. Idempotent — re-adding an existing partner returns the existing row.</summary>
    Task<Result<SharedBanTrustedChannelDto>> AddTrustedChannelAsync(
        Guid broadcasterId,
        Guid actorUserId,
        Guid trustedChannelId,
        CancellationToken ct = default
    );

    /// <summary>Removes a partner from the trust list; <c>NOT_FOUND</c> if absent.</summary>
    Task<Result> RemoveTrustedChannelAsync(
        Guid broadcasterId,
        Guid actorUserId,
        Guid trustedChannelId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Applies one inbound shared-chat ban to <paramref name="partnerBroadcasterId"/> IFF the partner
    /// opted in (<c>AcceptSharedChatBans</c>), trusts the origin (J.9a), and is verified to be in the
    /// SAME active shared-chat session — the predicate is enforced HERE, never by the caller. On apply:
    /// bans via the partner's own tenant token and records a <c>ModerationAction(ban,
    /// origin=federation, OriginChannelId)</c> row. A failed predicate is a truthful
    /// <c>Skipped(reason)</c>, never an error.
    /// </summary>
    Task<Result<SharedBanApplicationResult>> ApplyInboundSharedBanAsync(
        Guid partnerBroadcasterId,
        NomNomzBot.Domain.Moderation.Events.SharedChatBanIssuedEvent inbound,
        CancellationToken ct = default
    );

    /// <summary>
    /// Applies one inbound CROSS-INSTANCE federated ban to <paramref name="targetBroadcasterId"/>. This is a
    /// DIFFERENT trust plane from <see cref="ApplyInboundSharedBanAsync"/> (federation-oidc.md §6): the
    /// precondition is a verified NomNomzBot federation relationship — a trusted <c>FederationPeers</c> entry, a
    /// valid signed envelope, and the channel's <c>ChannelFederationOptIns</c> opt-in — all already enforced
    /// upstream by the federation inbound gateway. So this path requires NO active Twitch shared-chat session and
    /// consults NO local shared-chat trust list; it bans on the channel's OWN tenant token and records a provenance
    /// row with <c>Origin=federation</c> (explicitly distinct from <c>Origin=shared_chat</c>). A Twitch ban failure
    /// is a truthful <c>Skipped(reason)</c>, never an error. Idempotency per <c>(EventId, target)</c> is the
    /// caller's (the federation handler's) responsibility.
    /// </summary>
    Task<Result<SharedBanApplicationResult>> ApplyInboundFederatedBanAsync(
        Guid targetBroadcasterId,
        NomNomzBot.Domain.Moderation.Events.SharedChatBanIssuedEvent inbound,
        CancellationToken ct = default
    );
}
