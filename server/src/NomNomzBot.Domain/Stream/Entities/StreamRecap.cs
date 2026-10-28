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

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Stream.Entities;

/// <summary>
/// The end credits of one stream, kept: the recap the bot built for it (the raid recap widget's numbers), so a
/// streamer can read every stream's recap back later. One row per stream: a second raid in the same stream
/// rebuilds the recap and overwrites this row.
/// </summary>
public class StreamRecap : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ChannelId { get; set; }

    [MaxLength(50)]
    public string StreamId { get; set; } = null!;

    /// <summary>Start of the window the recap counts (UTC): the stream start.</summary>
    public DateTime FromAt { get; set; }

    /// <summary>End of the window the recap counts (UTC): the stream end, or the moment it was built while live.</summary>
    public DateTime ToAt { get; set; }

    /// <summary>The recap data, the same JSON the raid recap widget receives (a <c>StreamStatsDto</c>).</summary>
    public string DataJson { get; set; } = null!;

    /// <summary>Width in pixels of the stream output (OBS output resolution) when the recap was made live; null for a recap made without it.</summary>
    public int? OutputWidth { get; set; }

    /// <summary>Height in pixels of the stream output when the recap was made live; null for a recap made without it.</summary>
    public int? OutputHeight { get; set; }
}
