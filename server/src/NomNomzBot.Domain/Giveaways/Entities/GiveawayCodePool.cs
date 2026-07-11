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

namespace NomNomzBot.Domain.Giveaways.Entities;

/// <summary>
/// A named pool of prize codes (giveaways.md G.9) — game keys, discount codes — that a
/// <c>code_pool</c>-mode giveaway draws from. The codes themselves live in
/// <see cref="GiveawayCode"/> rows, AEAD-encrypted (D6).
/// </summary>
public class GiveawayCodePool : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }
}
