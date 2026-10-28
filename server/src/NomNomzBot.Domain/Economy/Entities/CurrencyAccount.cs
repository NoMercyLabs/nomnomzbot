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

    /// <summary>
    /// The tenant-stream position of the last ledger event the balance projection folded into this wallet.
    /// A fold at or below it is a re-apply (an operator replay, a driver retry) and must change nothing, or
    /// the lifetime totals would count the same event twice. Zeroed by the projection's reset.
    /// </summary>
    public long LastAppliedStreamPosition { get; set; }
}
