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
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Domain.Platform.Entities;

public class Storage : BaseEntity
{
    public int Id { get; set; }

    // FK→Channels.Id (tenant), null = global storage. string→Guid? per schema §1.1.
    public Guid? BroadcasterId { get; set; }

    [MaxLength(255)]
    public string Key { get; set; } = null!;

    public string? Value { get; set; }

    [MaxLength(4096)]
    public string? SecureValue { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel? Channel { get; set; }
}
