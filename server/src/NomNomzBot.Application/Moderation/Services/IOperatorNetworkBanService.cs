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

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// The operator-scoped "ban everywhere I moderate" fan-out (chat-client.md §3.5). It resolves the actor's channel
/// set from Twitch's authority — Get Moderated Channels for the operator, not the local DB — and bans the target in
/// each AS THE OPERATOR (their own token, <c>moderator_id</c> = them). Twitch is the sole gate: the operator can only
/// ban where Twitch already made them a moderator, so there is zero privilege escalation.
/// <para>
/// Distinct from <c>moderation:nuke</c> (a SuperMod PLATFORM power over tenant channels regardless of the actor's
/// per-channel Twitch mod status) and from the single-channel <see cref="IModerationService.BanAsync"/>. Best-effort:
/// a channel that fails (rate-limit, no longer a mod, missing scope) is recorded in the result, never aborts the rest.
/// </para>
/// </summary>
public interface IOperatorNetworkBanService
{
    Task<Result<NetworkBanResult>> BanAcrossModeratedAsync(
        Guid operatorUserId,
        string targetTwitchUserId,
        string? reason,
        CancellationToken ct = default
    );

    /// <summary>
    /// The reversal of <see cref="BanAcrossModeratedAsync"/> — lifts the target's ban across every channel Twitch
    /// says the operator moderates, AS THE OPERATOR, best-effort per channel (a channel that fails is recorded and
    /// the sweep continues).
    /// </summary>
    Task<Result<NetworkBanResult>> UnbanAcrossModeratedAsync(
        Guid operatorUserId,
        string targetTwitchUserId,
        CancellationToken ct = default
    );
}

/// <summary>The outcome of a network ban: how many channels were attempted, how many succeeded, and per-channel detail.</summary>
public sealed record NetworkBanResult(
    int Attempted,
    int Succeeded,
    IReadOnlyList<ChannelBanOutcome> Channels
);

/// <summary>One channel's ban outcome — its login, whether the ban landed, and the error when it did not.</summary>
public sealed record ChannelBanOutcome(string BroadcasterLogin, bool Succeeded, string? Error);
