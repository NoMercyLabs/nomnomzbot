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

namespace NomNomzBot.Domain.Giveaways.Entities;

/// <summary>
/// A named pool of prize codes (giveaways.md G.9) — game keys, discount codes — that a
/// <c>code_pool</c>-mode giveaway draws from. The codes themselves live in
/// <see cref="GiveawayCode"/> rows, AEAD-encrypted (D6).
/// </summary>
public class GiveawayCodePool : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = MonotonicGuid.Create();

    public Guid BroadcasterId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }
}
