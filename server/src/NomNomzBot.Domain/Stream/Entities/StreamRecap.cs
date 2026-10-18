// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
}
