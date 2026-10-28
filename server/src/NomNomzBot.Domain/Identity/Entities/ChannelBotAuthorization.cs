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

using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

public class ChannelBotAuthorization : SoftDeletableEntity, ITenantScoped
{
    // Surrogate UUIDv7 PK (schema E.4) — was int, re-keyed to Guid.
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // Tenant key (FK→Channels.Id) — string→Guid per schema §1.1.
    public Guid BroadcasterId { get; set; }

    // The bot identity authorized for this channel (FK→BotAccount, schema E.4).
    public Guid BotAccountId { get; set; }

    public DateTime AuthorizedAt { get; set; }

    public Guid? AuthorizedByUserId { get; set; }

    public DateTime? BotJoinedAt { get; set; }

    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(BotAccountId))]
    public virtual BotAccount BotAccount { get; set; } = null!;
}
