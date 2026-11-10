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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.YouTube;

/// <summary>
/// The channel's YouTube members, read with the streamer's grant (scope set <c>youtube.members</c>). A
/// member is to YouTube what a subscriber is to Twitch.
/// </summary>
public interface IYouTubeMembersService
{
    /// <summary>
    /// One page of the current members, newest first, in the order YouTube gives. The first page (no
    /// <paramref name="pageToken"/>) also names the channel's membership levels. A channel that is not
    /// connected, did not allow the member list, or has no memberships is a normal state: the answer says so
    /// in <see cref="YouTubeMembersDto.Access"/> and holds no members.
    /// </summary>
    Task<Result<YouTubeMembersDto>> GetMembersAsync(
        Guid broadcasterId,
        string? pageToken,
        int take,
        CancellationToken ct = default
    );

    /// <summary>
    /// The number of current members, kept for a few minutes. A channel whose members cannot be read is a
    /// failure, so a caller shows no number instead of a zero.
    /// </summary>
    Task<Result<int>> GetMemberCountAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <summary>Whether the bot may read the channel's members. Text values, so the dashboard reads the word.</summary>
public static class YouTubeMembersAccess
{
    /// <summary>The members were read.</summary>
    public const string Allowed = "allowed";

    /// <summary>The channel has no YouTube connection.</summary>
    public const string NotConnected = "not_connected";

    /// <summary>The streamer did not allow the member list, or Google gave this app no access to it.</summary>
    public const string NotAllowed = "not_allowed";

    /// <summary>The YouTube channel has no channel memberships.</summary>
    public const string NotEnabled = "not_enabled";
}

/// <summary>
/// One channel member. The channel id and the picture are null for a member whose own channel is gone.
/// </summary>
public sealed record YouTubeMemberDto(
    string? ChannelId,
    string DisplayName,
    string? ProfileImageUrl,
    string? LevelId,
    string? LevelName,
    DateTimeOffset? MemberSince,
    int TotalMonths
);

/// <summary>The channel's member count; null when the bot cannot read the members.</summary>
public sealed record YouTubeMemberCountDto(int? Count);

/// <summary>One membership level of the channel.</summary>
public sealed record YouTubeMembershipLevelDto(string Id, string Name);

/// <summary>
/// One page of members. <paramref name="Access"/> is one of <see cref="YouTubeMembersAccess"/>;
/// <paramref name="Total"/> is the member count YouTube reports for the whole list.
/// </summary>
public sealed record YouTubeMembersDto(
    string Access,
    IReadOnlyList<YouTubeMemberDto> Members,
    IReadOnlyList<YouTubeMembershipLevelDto> Levels,
    int Total,
    string? NextPageToken
);
