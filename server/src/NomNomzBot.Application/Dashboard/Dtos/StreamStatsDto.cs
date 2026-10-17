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
    [JsonPropertyName("stream")]
    public required StreamStatsWindowDto Stream { get; init; }

    /// <summary>Seconds from the start of the stream to its end (or to now while live).</summary>
    [JsonPropertyName("duration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Duration { get; set; }

    [JsonPropertyName("peakViewers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PeakViewers { get; set; }

    [JsonPropertyName("followers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Followers { get; set; }

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

    [JsonPropertyName("raidsIn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StreamStatsRaidDto>? RaidsIn { get; set; }

    /// <summary>Redemption total and the top reward titles.</summary>
    [JsonPropertyName("redemptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsRankedDto? Redemptions { get; set; }

    /// <summary>Song request total and the top requesters; never song titles.</summary>
    [JsonPropertyName("songRequesters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsRankedDto? SongRequesters { get; set; }

    [JsonPropertyName("chat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsChatDto? Chat { get; set; }

    [JsonPropertyName("topChatters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StreamStatsNamedCountDto>? TopChatters { get; set; }

    /// <summary>Commands run in the window and the most-used ones.</summary>
    [JsonPropertyName("commandsRun")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StreamStatsRankedDto? CommandsRun { get; set; }
}

public sealed record StreamStatsWindowDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("endedAt")]
    public DateTimeOffset? EndedAt { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("game")]
    public string? Game { get; init; }
}

public sealed record StreamStatsSubsDto
{
    [JsonPropertyName("new")]
    public int New { get; init; }

    [JsonPropertyName("resub")]
    public int Resub { get; init; }

    [JsonPropertyName("gift")]
    public int Gift { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

public sealed record StreamStatsNamedCountDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

/// <summary>A total and its top entries, ranked by count then name.</summary>
public sealed record StreamStatsRankedDto
{
    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("top")]
    public required List<StreamStatsNamedCountDto> Top { get; init; }
}

public sealed record StreamStatsRaidDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("viewers")]
    public int Viewers { get; init; }
}

public sealed record StreamStatsChatDto
{
    [JsonPropertyName("messages")]
    public int Messages { get; init; }

    [JsonPropertyName("uniqueChatters")]
    public int UniqueChatters { get; init; }
}
