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

public class ChannelFeature : BaseEntity, ITenantScoped
{
    public int Id { get; set; }
    public Guid BroadcasterId { get; set; }

    [MaxLength(50)]
    public string FeatureKey { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public DateTime? EnabledAt { get; set; }

    public string[] RequiredScopes { get; set; } = [];

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
