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

namespace NomNomzBot.Domain.Discord.Entities;

public class DiscordServerAuthorization : BaseEntity
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string BroadcasterId { get; set; } = null!;

    [MaxLength(50)]
    public string GuildId { get; set; } = null!;

    [MaxLength(255)]
    public string GuildName { get; set; } = null!;

    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    [MaxLength(50)]
    public string? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
