// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.Persistence;

/// <summary>Schema — the network-wide block ledger (S-ADMIN-8b).</summary>
public class NetworkBlockConfiguration : IEntityTypeConfiguration<NetworkBlock>
{
    public void Configure(EntityTypeBuilder<NetworkBlock> builder)
    {
        builder.HasKey(e => e.Id);

        // The enforcement gate's own lookup: "is this user network-blocked right now" — one index.
        builder.HasIndex(e => new { e.TargetUserId, e.Status });
    }
}
