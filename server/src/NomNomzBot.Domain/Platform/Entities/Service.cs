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

namespace NomNomzBot.Domain.Platform.Entities;

public class Service : BaseEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(50)]
    public string Name { get; set; } = null!;

    public bool Enabled { get; set; } = true;

    // Tenant key (FK→Channels.Id), null for the platform/shared-bot row.
    // string→Guid? per schema §1.1. NOT a Twitch id — the Twitch user id stays in UserId.
    public Guid? BroadcasterId { get; set; }

    [MaxLength(512)]
    public string? ClientId { get; set; }

    [MaxLength(512)]
    public string? ClientSecret { get; set; }

    [MaxLength(255)]
    public string? UserName { get; set; }

    [MaxLength(50)]
    public string? UserId { get; set; }

    public string[] Scopes { get; set; } = [];

    [MaxLength(2048)]
    public string? AccessToken { get; set; }

    [MaxLength(2048)]
    public string? RefreshToken { get; set; }

    public DateTime? TokenExpiry { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel? Channel { get; set; }
}
