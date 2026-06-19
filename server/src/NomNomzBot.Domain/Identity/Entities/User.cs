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

public class User : BaseEntity
{
    [MaxLength(50)]
    public string Id { get; set; } = null!;

    [MaxLength(255)]
    public string Username { get; set; } = null!;

    [MaxLength(255)]
    public string DisplayName { get; set; } = null!;

    [MaxLength(255)]
    public string? NickName { get; set; }

    [MaxLength(50)]
    public string? Timezone { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(2048)]
    public string? ProfileImageUrl { get; set; }

    [MaxLength(2048)]
    public string? OfflineImageUrl { get; set; }

    [MaxLength(7)]
    public string? Color { get; set; }

    [MaxLength(50)]
    public string BroadcasterType { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public bool IsAdmin { get; set; }

    public Pronoun? Pronoun { get; set; }
    public bool PronounManualOverride { get; set; }

    public virtual Channel? Channel { get; set; }
}
