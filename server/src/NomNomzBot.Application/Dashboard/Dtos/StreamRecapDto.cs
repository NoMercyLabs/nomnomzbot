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

    /// <summary>The recap data, exactly as the widget got it.</summary>
    [JsonPropertyName("stats")]
    public required StreamStatsDto Stats { get; init; }
}
