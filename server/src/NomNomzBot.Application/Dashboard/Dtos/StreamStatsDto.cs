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
/// The answer to <c>stream-stats</c>: the stream window plus one named property per requested metric key. A metric
/// that was not requested (or has nothing to show, such as the peak of a stream that never recorded one) is omitted.
/// A new metric adds a property here and a class implementing <c>IStreamMetric</c>; never an endpoint.
/// </summary>
public sealed record StreamStatsDto
{
    /// <summary>The stream these numbers belong to.</summary>
    [JsonPropertyName("stream")]
    public required StreamStatsWindowDto Stream { get; init; }

    /// <summary>Who streamed: the channel's display name and profile picture, for headers and watermarks.</summary>
    [JsonPropertyName("channel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsChannelDto? Channel { get; set; }

    /// <summary>Seconds from the start of the stream to its end (or to now while live).</summary>
    [JsonPropertyName("duration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Duration { get; set; }

    /// <summary>The highest viewer count the stream recorded.</summary>
    [JsonPropertyName("peakViewers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PeakViewers { get; set; }

    /// <summary>New followers during the stream.</summary>
    [JsonPropertyName("followers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Followers { get; set; }

    /// <summary>Subscriptions during the stream: new, resubscribed, gifted and the total.</summary>
    [JsonPropertyName("subs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsSubsDto? Subs { get; set; }

    /// <summary>Gifters by gift count; anonymous gifts are merged as "Anonymous".</summary>
    [JsonPropertyName("gifters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StreamStatsNamedCountDto>? Gifters { get; set; }

    /// <summary>Viewers who subscribed or resubscribed themselves (gift recipients excluded), in order.</summary>
    [JsonPropertyName("selfSubs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SelfSubs { get; set; }

    /// <summary>Total bits and the top cheerers (count = bits).</summary>
    [JsonPropertyName("bits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsRankedDto? Bits { get; set; }

    /// <summary>Raids that arrived during the stream: who raided and with how many viewers.</summary>
    [JsonPropertyName("raidsIn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StreamStatsRaidDto>? RaidsIn { get; set; }

    /// <summary>The hype trains that ended during the stream. Absent when there was none.</summary>
    [JsonPropertyName("hypeTrain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsHypeTrainDto? HypeTrain { get; set; }

    /// <summary>Redemption total and the top reward titles.</summary>
    [JsonPropertyName("redemptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsRankedDto? Redemptions { get; set; }

    /// <summary>Song request total and the top requesters; never song titles.</summary>
    [JsonPropertyName("songRequesters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsRankedDto? SongRequesters { get; set; }

    /// <summary>Chat message count and the number of different chatters.</summary>
    [JsonPropertyName("chat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsChatDto? Chat { get; set; }

    /// <summary>The chatters with the most messages.</summary>
    [JsonPropertyName("topChatters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StreamStatsNamedCountDto>? TopChatters { get; set; }

    /// <summary>Commands run in the window and the most-used ones.</summary>
    [JsonPropertyName("commandsRun")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsRankedDto? CommandsRun { get; set; }
}

/// <summary>The window of one stream: its id, start, end, title and game.</summary>
public sealed record StreamStatsWindowDto
{
    /// <summary>The stream id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>When the stream started.</summary>
    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When the stream ended. Null while it is live.</summary>
    [JsonPropertyName("endedAt")]
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>The stream title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>The game or category name.</summary>
    [JsonPropertyName("game")]
    public string? Game { get; init; }
}

/// <summary>The channel a stream belongs to.</summary>
public sealed record StreamStatsChannelDto
{
    /// <summary>The channel display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The channel profile picture URL. Null when the platform gave none.</summary>
    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; init; }
}

/// <summary>The hype trains that ended during a stream.</summary>
public sealed record StreamStatsHypeTrainDto
{
    /// <summary>The highest level any hype train reached.</summary>
    [JsonPropertyName("level")]
    public required int Level { get; init; }

    /// <summary>How many hype trains ended during the stream.</summary>
    [JsonPropertyName("trains")]
    public required int Trains { get; init; }
}

/// <summary>Subscription counts of a stream.</summary>
public sealed record StreamStatsSubsDto
{
    /// <summary>First-time subscriptions.</summary>
    [JsonPropertyName("new")]
    public int New { get; init; }

    /// <summary>Resubscriptions.</summary>
    [JsonPropertyName("resub")]
    public int Resub { get; init; }

    /// <summary>Gifted subscriptions.</summary>
    [JsonPropertyName("gift")]
    public int Gift { get; init; }

    /// <summary>All subscriptions together.</summary>
    [JsonPropertyName("total")]
    public int Total { get; init; }
}

/// <summary>A name with a count.</summary>
public sealed record StreamStatsNamedCountDto
{
    /// <summary>The viewer or reward name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>How many times it counted.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }
}

/// <summary>A total and its top entries, ranked by count then name.</summary>
public sealed record StreamStatsRankedDto
{
    /// <summary>The total across all entries.</summary>
    [JsonPropertyName("total")]
    public int Total { get; init; }

    /// <summary>The top entries, highest first.</summary>
    [JsonPropertyName("top")]
    public required List<StreamStatsNamedCountDto> Top { get; init; }
}

/// <summary>One raid that arrived.</summary>
public sealed record StreamStatsRaidDto
{
    /// <summary>The raiding channel.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>How many viewers came with the raid.</summary>
    [JsonPropertyName("viewers")]
    public int Viewers { get; init; }
}

/// <summary>Chat activity of a stream.</summary>
public sealed record StreamStatsChatDto
{
    /// <summary>The number of chat messages.</summary>
    [JsonPropertyName("messages")]
    public int Messages { get; init; }

    /// <summary>The number of different chatters.</summary>
    [JsonPropertyName("uniqueChatters")]
    public int UniqueChatters { get; init; }
}
