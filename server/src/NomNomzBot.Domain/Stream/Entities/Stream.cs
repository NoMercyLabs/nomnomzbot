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
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Stream.Entities;

public class Stream : BaseEntity
{
    [MaxLength(50)]
    public string Id { get; set; } = null!;

    // FK→Channels.Id (tenant) — string→Guid per schema §1.1.
    public Guid ChannelId { get; set; }

    [MaxLength(50)]
    public string? Language { get; set; }

    [MaxLength(50)]
    public string? GameId { get; set; }

    [MaxLength(255)]
    public string? GameName { get; set; }

    [MaxLength(255)]
    public string? Title { get; set; }

    public int Delay { get; set; }

    public List<string> Tags { get; set; } = [];
    public List<string> ContentLabels { get; set; } = [];

    public bool IsBrandedContent { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Highest concurrent viewer count observed during THIS stream — folded max from the live
    /// viewer-count samples (StreamStatusPollingService). Null when no sample ever landed (e.g. the
    /// bot joined after the stream ended). The per-stream twin of ChannelAnalyticsDaily.PeakViewers.
    /// </summary>
    public int? PeakViewers { get; set; }

    [ForeignKey(nameof(ChannelId))]
    public virtual Channel Channel { get; set; } = null!;
}
