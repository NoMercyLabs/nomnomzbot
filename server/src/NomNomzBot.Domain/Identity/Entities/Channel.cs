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
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Domain.Identity.Entities;

public class Channel : SoftDeletableEntity
{
    [MaxLength(50)]
    public string Id { get; set; } = null!;

    [MaxLength(25)]
    public string Name { get; set; } = null!;

    public bool Enabled { get; set; } = true;

    [MaxLength(450)]
    public string? ShoutoutTemplate { get; set; }

    public DateTime? LastShoutout { get; set; }

    public int ShoutoutInterval { get; set; } = 10;

    [MaxLength(100)]
    public string? UsernamePronunciation { get; set; }

    public bool IsOnboarded { get; set; }

    public DateTime? BotJoinedAt { get; set; }

    [MaxLength(36)]
    public string OverlayToken { get; set; } = Guid.NewGuid().ToString();

    public bool IsLive { get; set; }

    [MaxLength(50)]
    public string? Language { get; set; }

    [MaxLength(50)]
    public string? GameId { get; set; }

    [MaxLength(255)]
    public string? GameName { get; set; }

    [MaxLength(255)]
    public string? Title { get; set; }

    public int StreamDelay { get; set; }

    public List<string> Tags { get; set; } = [];
    public List<string> ContentLabels { get; set; } = [];

    public bool IsBrandedContent { get; set; }

    [ForeignKey(nameof(Id))]
    public virtual User User { get; set; } = null!;

    public virtual ICollection<ChannelModerator> Moderators { get; set; } = [];
    public virtual ICollection<global::NomNomzBot.Domain.Stream.Entities.Stream> Streams { get; set; } = [];
    public virtual ICollection<ChannelEvent> Events { get; set; } = [];
}
