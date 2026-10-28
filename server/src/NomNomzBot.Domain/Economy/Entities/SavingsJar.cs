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
/// A pooled cross-channel savings account (economy.md K.4). CROSS-TENANT: deliberately NOT
/// <c>ITenantScoped</c> — access is guarded by the membership predicate (an accepted
/// <c>SavingsJarMembership</c> for the acting tenant), enforced in <c>ISavingsJarService</c>, not by the global
/// tenant filter. Owned by one channel; partner channels join by mutual-consent invite.
/// </summary>
public class SavingsJar : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OwnerBroadcasterId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long? GoalAmount { get; set; }
    public long Balance { get; set; }
    public string? IconUrl { get; set; }
    public bool IsOpen { get; set; }
    public long? MaxWithdrawalPerChannel { get; set; }
}
