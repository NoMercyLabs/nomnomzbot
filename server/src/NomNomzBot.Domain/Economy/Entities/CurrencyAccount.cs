// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// A viewer's wallet in one channel (economy.md K.2). <c>Balance</c> is the running projection of the
/// append-only ledger; <c>LifetimeEarned</c>/<c>LifetimeSpent</c> are cumulative. One per
/// <c>(BroadcasterId, ViewerUserId)</c>. <c>ViewerTwitchUserId</c> is a PII-hash column.
/// </summary>
public class CurrencyAccount : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid ViewerUserId { get; set; }
    public string ViewerTwitchUserId { get; set; } = null!;
    public long Balance { get; set; }
    public long LifetimeEarned { get; set; }
    public long LifetimeSpent { get; set; }
    public bool IsFrozen { get; set; }
    public DateTime? LastActivityAt { get; set; }
}
