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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

/// <summary>
/// Maps the DEK registry (<see cref="CryptoKey"/>, schema Q.1). Indexed for the two hot lookups the crypto core
/// runs: by id (the FK target → <c>GetAsync</c>) and by the <c>(KeyScope, BroadcasterId, SubjectIdHash, Status)</c>
/// identity (<c>GetActiveByIdentityAsync</c>). The "at most one ACTIVE key per identity" invariant is enforced in
/// <c>CryptoKeySubjectKeyStore</c> (a shredded row keeps the same identity, so a unique DB constraint would block
/// the spec-required fresh-key re-entry after a crypto-shred); it is a non-unique covering index here.
/// </summary>
public class CryptoKeyConfiguration : IEntityTypeConfiguration<CryptoKey>
{
    public void Configure(EntityTypeBuilder<CryptoKey> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.KeyScope).IsRequired().HasMaxLength(20);
        builder.Property(e => e.SubjectIdHash).HasMaxLength(64);
        builder.Property(e => e.KekReference).HasMaxLength(255);
        builder.Property(e => e.Provider).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Algorithm).IsRequired().HasMaxLength(30);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);

        builder.HasIndex(e => e.BroadcasterId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.DestroyedAt);

        // Covers GetActiveByIdentityAsync — the per-boot lookup that re-resolves a stored DEK by its stable
        // identity. Non-unique: a destroyed key shares the identity of its active successor by design.
        builder.HasIndex(e => new
        {
            e.KeyScope,
            e.BroadcasterId,
            e.SubjectIdHash,
            e.Status,
        });
    }
}
