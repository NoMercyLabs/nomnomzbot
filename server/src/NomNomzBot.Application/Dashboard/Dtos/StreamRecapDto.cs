// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace NomNomzBot.Application.Dashboard.Dtos;

/// <summary>
/// What happened in one stream, counted from recorded data inside the stream window (<c>StartedAt</c> to
/// <c>EndedAt</c>, or now while live). Returned by GET /api/v1/dashboard/{channelId}/stream-recap. A stat the bot
/// does not record (average viewers, new chatters, commands run) is not part of this contract.
/// </summary>
public sealed record StreamRecapDto
{
    [JsonPropertyName("stream")]
    public required StreamRecapStreamDto Stream { get; init; }

    /// <summary>New followers inside the window.</summary>
    [JsonPropertyName("followers")]
    public int Followers { get; init; }

    [JsonPropertyName("subs")]
    public required StreamRecapSubsDto Subs { get; init; }

    /// <summary>Gifters inside the window, most gifts first. Anonymous gifters merge into one "Anonymous" entry.</summary>
    [JsonPropertyName("giftSubs")]
    public required List<StreamRecapGifterDto> GiftSubs { get; init; }

    [JsonPropertyName("bits")]
    public required StreamRecapBitsDto Bits { get; init; }

    [JsonPropertyName("raidsIn")]
    public required List<StreamRecapRaidDto> RaidsIn { get; init; }

    [JsonPropertyName("redemptions")]
    public required StreamRecapRedemptionsDto Redemptions { get; init; }

    /// <summary>Song requests accepted inside the window.</summary>
    [JsonPropertyName("songs")]
    public required StreamRecapSongsDto Songs { get; init; }

    [JsonPropertyName("chat")]
    public required StreamRecapChatDto Chat { get; init; }
}

public sealed record StreamRecapStreamDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Null while the stream is still live.</summary>
    [JsonPropertyName("endedAt")]
    public DateTimeOffset? EndedAt { get; init; }

    [JsonPropertyName("durationSeconds")]
    public long DurationSeconds { get; init; }

    /// <summary>Null when no viewer-count sample ever landed.</summary>
    [JsonPropertyName("peakViewers")]
    public int? PeakViewers { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("game")]
    public string? Game { get; init; }
}

public sealed record StreamRecapSubsDto
{
    /// <summary>First-time subscriptions that were not gifts.</summary>
    [JsonPropertyName("new")]
    public int New { get; init; }

    [JsonPropertyName("resub")]
    public int Resub { get; init; }

    /// <summary>Subscriptions given away (the sum of each gift event's count).</summary>
    [JsonPropertyName("gift")]
    public int Gift { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

public sealed record StreamRecapGifterDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

public sealed record StreamRecapBitsDto
{
    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("top")]
    public required List<StreamRecapCheererDto> Top { get; init; }
}

public sealed record StreamRecapCheererDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("bits")]
    public int Bits { get; init; }
}

public sealed record StreamRecapRaidDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("viewers")]
    public int Viewers { get; init; }
}

public sealed record StreamRecapRedemptionsDto
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("top")]
    public required List<StreamRecapRedemptionTitleDto> Top { get; init; }
}

public sealed record StreamRecapRedemptionTitleDto
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

public sealed record StreamRecapSongsDto
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("recent")]
    public required List<StreamRecapSongDto> Recent { get; init; }
}

public sealed record StreamRecapSongDto
{
    [JsonPropertyName("track")]
    public required string Track { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }
}

public sealed record StreamRecapChatDto
{
    [JsonPropertyName("messages")]
    public int Messages { get; init; }

    [JsonPropertyName("uniqueChatters")]
    public int UniqueChatters { get; init; }
}
