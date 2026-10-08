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
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Content.Moderation;

/// <summary>
/// Seeds the curated spam corpus (spam-defense.md §4.1, Order 10 — instance-wide reference data with no FK
/// dependencies) as <see cref="SpamSignature"/> rows with <see cref="SignatureSource.Curated"/>, which skip
/// quarantine and may act at once. The merge follows the review desk's curation rule
/// (<c>TrustSafetyReviewService.CurateSignatureAsync</c>) so a signature has one meaning however it is curated:
/// <list type="bullet">
///   <item>No row for the (kind, value): add it as Curated.</item>
///   <item>A withdrawn or soft-deleted row: leave it alone. A moderator's verdict is never overruled by a seed.</item>
///   <item>A Local or Network row: upgrade it in place to Curated (lifting quarantine), never a second row.</item>
///   <item>An already Curated row: untouched, so a re-run changes nothing.</item>
/// </list>
/// The seeder only tracks changes; the SeedRunner commits them in its single transaction.
/// </summary>
public sealed class SpamCorpusSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _time;

    public SpamCorpusSeeder(IApplicationDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public int Order => 10;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        SpamSeedCorpus corpus = SpamSeedCorpus.Load();
        DateTime now = _time.GetUtcNow().UtcDateTime;

        await MergeAsync(SignatureKind.Skeleton, corpus.Skeletons, now, ct);
        await MergeAsync(SignatureKind.Domain, corpus.Domains, now, ct);
    }

    private async Task MergeAsync(
        SignatureKind kind,
        IReadOnlyList<string> values,
        DateTime now,
        CancellationToken ct
    )
    {
        Dictionary<string, SpamSignature> existing = await _db
            .SpamSignatures.IgnoreQueryFilters()
            .Where(sig => sig.Kind == kind && values.Contains(sig.Value))
            .ToDictionaryAsync(sig => sig.Value, ct);

        foreach (string value in values)
        {
            if (!existing.TryGetValue(value, out SpamSignature? row))
            {
                _db.SpamSignatures.Add(NewCuratedRow(kind, value, now));
                continue;
            }

            if (row.WithdrawnAt is not null || row.DeletedAt is not null)
                continue;

            if (row.Source != SignatureSource.Curated)
                Upgrade(row, now);
        }
    }

    private static SpamSignature NewCuratedRow(SignatureKind kind, string value, DateTime now) =>
        new()
        {
            Kind = kind,
            Value = value,
            Source = SignatureSource.Curated,
            IsQuarantined = false,
            Corroborations = 1,
            FirstSeenAt = now,
            LastConfirmedAt = now,
        };

    private static void Upgrade(SpamSignature row, DateTime now)
    {
        row.Source = SignatureSource.Curated;
        row.IsQuarantined = false;
        row.Corroborations++;
        row.LastConfirmedAt = now;
    }
}
