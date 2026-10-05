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
/// A moderator's list ban across every channel they moderate, one batch per channel (chat-client.md §3.5). Bans
/// run at once in the moderator's own channel, in the attacked channel(s), and in every channel that is offline.
/// Only a live channel is held back, so its stream is not disrupted: its chat is told (when the bot is there), and
/// the bans run when its broadcaster, a lead moderator or an editor types <c>!allow massban</c>, or when the stream
/// ends. <c>!disallow massban</c> stops them. A channel the moderator excludes, or whose owner turned mass bans off
/// (<c>Channel.AcceptsModeratorMassBans</c>), gets no batch at all.
/// </summary>
public interface IMassBanConsentService
{
    /// <summary>
    /// Lists every channel the moderator moderates with the status a request would give it, so channels can be
    /// excluded before anything is banned. Fails when the channels or their live state cannot be read from Twitch.
    /// </summary>
    Task<Result<IReadOnlyList<MassBanChannelPreview>>> PreviewAsync(
        Guid operatorUserId,
        MassBanScope scope,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates one batch per included channel, as described on the interface. Duplicate accounts are dropped.
    /// Fails when the list is empty, holds more than 1000 accounts or a non-numeric id, or when the moderated
    /// channels or their live state cannot be read from Twitch.
    /// </summary>
    Task<Result<MassBanSweepResult>> RequestAsync(
        Guid operatorUserId,
        string operatorDisplayName,
        IReadOnlyList<MassBanTarget> targets,
        MassBanScope scope,
        CancellationToken ct = default
    );

    /// <summary>Approves every held, undecided batch of the channel, so its bans run now.</summary>
    Task<MassBanDecision> ApproveAsync(
        Guid channelId,
        string decidedByDisplayName,
        CancellationToken ct = default
    );

    /// <summary>Declines every held, undecided batch of the channel; the accounts not yet banned stay unbanned.</summary>
    Task<MassBanDecision> DeclineAsync(
        Guid channelId,
        string decidedByDisplayName,
        CancellationToken ct = default
    );
}

/// <summary>One account to ban, with the reason Twitch shows (for example where and when it followed).</summary>
public sealed record MassBanTarget(string TwitchUserId, string Reason);

/// <summary>
/// Which channels a mass ban touches: the attacked channels ban at once even while live; the excluded channels
/// get nothing. Both are Twitch logins, matched without case.
/// </summary>
public sealed record MassBanScope(
    IReadOnlyCollection<string> AttackedChannelLogins,
    IReadOnlyCollection<string> ExcludedChannelLogins
);

/// <summary>One moderated channel and the <see cref="MassBanChannelStatus"/> a request would give it.</summary>
public sealed record MassBanChannelPreview(
    string BroadcasterId,
    string BroadcasterLogin,
    string Status,
    bool IsOwnChannel,
    bool IsAttacked,
    bool IsLive,
    bool UsesBot
);

/// <summary>What happened in each channel of a sweep.</summary>
public sealed record MassBanSweepResult(IReadOnlyList<MassBanChannelOutcome> Channels);

/// <summary>
/// One channel's outcome: its login, a <see cref="MassBanChannelStatus"/> value, and the number of accounts
/// queued there (0 for a channel that was skipped).
/// </summary>
public sealed record MassBanChannelOutcome(string BroadcasterLogin, string Status, int Accounts);

/// <summary>The answer to <c>!allow massban</c> or <c>!disallow massban</c>.</summary>
public sealed record MassBanDecision(MassBanDecisionStatus Status, int Accounts);

public enum MassBanDecisionStatus
{
    /// <summary>The channel has no open batch.</summary>
    NothingPending,

    Approved,

    Declined,
}

/// <summary>The status strings of <see cref="MassBanChannelOutcome.Status"/> and <see cref="MassBanChannelPreview.Status"/>.</summary>
public static class MassBanChannelStatus
{
    /// <summary>Own channel, attacked channel or offline channel: the bans run now.</summary>
    public const string Banning = "banning";

    /// <summary>A live channel with the bot: its chat is asked; the bans wait for an answer or the stream end.</summary>
    public const string AwaitingApproval = "awaiting_approval";

    /// <summary>A live channel whose chat cannot be asked: the bans run when the stream ends.</summary>
    public const string HeldUntilOffline = "held_until_offline";

    /// <summary>The moderator left this channel out.</summary>
    public const string Excluded = "excluded";

    /// <summary>The channel's owner turned mass bans from moderators off.</summary>
    public const string OptedOut = "opted_out";
}
