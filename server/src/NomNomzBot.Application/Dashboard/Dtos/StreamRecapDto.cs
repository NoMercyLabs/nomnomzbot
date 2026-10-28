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

using System.Text.Json.Serialization;

namespace NomNomzBot.Application.Dashboard.Dtos;

/// <summary>One stream's kept recap: the window it counts and the same data the raid recap widget receives.</summary>
public sealed record StreamRecapDto
{
    /// <summary>The stream the recap belongs to.</summary>
    [JsonPropertyName("streamId")]
    public required string StreamId { get; init; }

    /// <summary>Start of the window the recap counts.</summary>
    [JsonPropertyName("fromAt")]
    public DateTimeOffset FromAt { get; init; }

    /// <summary>End of the window the recap counts.</summary>
    [JsonPropertyName("toAt")]
    public DateTimeOffset ToAt { get; init; }

    /// <summary>When the recap was last built.</summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Width in pixels of the stream output the recap was made at; null when none was stored (the dashboard labels its fallback).</summary>
    [JsonPropertyName("outputWidth")]
    public int? OutputWidth { get; init; }

    /// <summary>Height in pixels of the stream output the recap was made at; null when none was stored.</summary>
    [JsonPropertyName("outputHeight")]
    public int? OutputHeight { get; init; }

    /// <summary>The recap data, exactly as the widget got it.</summary>
    [JsonPropertyName("stats")]
    public required StreamStatsDto Stats { get; init; }
}
