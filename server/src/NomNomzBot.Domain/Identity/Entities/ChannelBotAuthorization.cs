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
