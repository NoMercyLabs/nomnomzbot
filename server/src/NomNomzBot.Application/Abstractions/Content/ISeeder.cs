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

namespace NomNomzBot.Application.Abstractions.Content;

/// <summary>
/// A shipped-but-editable content seed (backend-structure §5 / D6). Each implementation
/// writes its default rows on startup. Discovered by the §4 assembly scan via this marker,
/// then run in <see cref="Order"/> sequence (not registration order) inside one transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordering contract (§5.1).</b> Reference data has real FK dependencies — a child
/// table's rows cannot be written before its parent's. A seeder MUST therefore declare an
/// <see cref="Order"/> greater than every seeder whose rows it FK-references. Global reference
/// data with no dependencies sits low (e.g. 10); FK-dependent data sits higher.
/// </para>
/// <para>
/// <b>Idempotency contract.</b> <see cref="SeedAsync"/> MUST upsert by a natural key, so the
/// single-fire startup seed and any re-run both leave exactly one row per natural key — never
/// a duplicate, never an error. The seed runner does NOT call <c>SaveChanges</c> between
/// seeders; each seeder either persists its own writes or leaves them tracked for the runner's
/// single transactional commit.
/// </para>
/// </remarks>
public interface ISeeder
{
    /// <summary>
    /// Execution position, ascending. Ties may run in any order. A seeder MUST order after
    /// every seeder whose rows it FK-references.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Seeds this content's default rows. MUST be idempotent: upsert by natural key so a
    /// re-run adds nothing and throws nothing.
    /// </summary>
    Task SeedAsync(CancellationToken ct = default);
}
