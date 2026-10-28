// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// A dashboard ban request (chat-client.md §3.5). <see cref="Scope"/> is <c>"this_channel"</c> (default) or
/// <c>"all_moderated"</c>. A <see cref="DurationSeconds"/> turns a <c>this_channel</c> action into a timeout;
/// <c>all_moderated</c> is always a permanent ban across every channel Twitch says the operator moderates.
/// </summary>
public sealed record BanUserRequest(
    string TargetTwitchUserId,
    string? Reason = null,
    int? DurationSeconds = null,
    string Scope = "this_channel"
);

/// <summary>
/// A dashboard unban request — the reversal of <see cref="BanUserRequest"/>. <see cref="Scope"/> is
/// <c>"this_channel"</c> (default) or <c>"all_moderated"</c>, which lifts the ban across every channel Twitch says
/// the operator moderates (the "un-nuke").
/// </summary>
public sealed record UnbanUserRequest(string TargetTwitchUserId, string Scope = "this_channel");

/// <summary>
/// The outcome of a ban for either scope. <c>this_channel</c> collapses to a one-row result; <c>all_moderated</c>
/// carries one <see cref="ChannelBanOutcomeDto"/> per channel the fan-out touched.
/// </summary>
public sealed record NetworkBanResultDto(
    int Attempted,
    int Succeeded,
    IReadOnlyList<ChannelBanOutcomeDto> Channels
);

/// <summary>One channel's ban outcome — its login, whether the ban landed, and the error when it did not.</summary>
public sealed record ChannelBanOutcomeDto(string BroadcasterLogin, bool Succeeded, string? Error);
