// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// A list of accounts to ban across every channel the caller moderates. <see cref="AttackedChannels"/> holds the
/// logins of the channel(s) under attack: those ban at once even while live, like the caller's own channel.
/// <see cref="ExcludedChannels"/> holds the logins the caller leaves out; those get no bans at all.
/// </summary>
public sealed record MassBanRequest(
    IReadOnlyList<MassBanTargetDto> Targets,
    IReadOnlyList<string>? AttackedChannels,
    IReadOnlyList<string>? ExcludedChannels
);

/// <summary>
/// One channel the caller moderates and what a mass ban would do there (the same status values as
/// <see cref="MassBanChannelOutcomeDto"/>), with the facts behind it.
/// </summary>
public sealed record MassBanChannelPreviewDto(
    string BroadcasterId,
    string BroadcasterLogin,
    string Status,
    string? OptIn,
    bool IsOwnChannel,
    bool IsAttacked,
    bool IsLive,
    bool UsesBot
);

/// <summary>Whether this channel takes part in moderators' mass bans across their channels (off until the owner says so).</summary>
public sealed record MassBanOptInDto(bool Accepts);

/// <summary>A streamer's permission as recorded by the operator, for a channel that may never have joined the bot.</summary>
public sealed record ModeratorMassBanOptInDto(
    string BroadcasterId,
    string BroadcasterLogin,
    string Note,
    DateTime RecordedAt
);

/// <summary>Where the permission came from, in the operator's words ("asked in Discord 2026-10-06").</summary>
public sealed record ModeratorMassBanOptInRequest(string? Note);

/// <summary>One account: its numeric Twitch user id and the optional reason Twitch shows.</summary>
public sealed record MassBanTargetDto(string TwitchUserId, string? Reason);

/// <summary>The outcome per channel of a mass-ban request.</summary>
public sealed record MassBanResultDto(IReadOnlyList<MassBanChannelOutcomeDto> Channels);

/// <summary>
/// One channel: its login, the status (<c>banning</c>, <c>awaiting_approval</c>, <c>held_until_offline</c>,
/// <c>excluded</c> or <c>not_opted_in</c>) and how many accounts its batch holds (0 when it was skipped).
/// </summary>
public sealed record MassBanChannelOutcomeDto(string BroadcasterLogin, string Status, int Accounts);
