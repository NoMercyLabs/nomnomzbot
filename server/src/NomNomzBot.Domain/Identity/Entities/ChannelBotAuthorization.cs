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

namespace NomNomzBot.Domain.Identity.Entities;

public class ChannelBotAuthorization : BaseEntity
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string BroadcasterId { get; set; } = null!;

    public DateTime AuthorizedAt { get; set; }

    [MaxLength(50)]
    public string? AuthorizedBy { get; set; }

    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
