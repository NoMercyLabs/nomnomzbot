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

using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>Collects the sample every live trigger offers, stamped with the clock's current time.</summary>
public sealed class TriggerSampleCatalog(
    IEnumerable<ITriggerSampleSource> sources,
    TimeProvider clock
) : ITriggerSampleCatalog
{
    public IReadOnlyList<TriggerSample> List()
    {
        DateTimeOffset now = clock.GetUtcNow();
        return
        [
            .. sources
                .Select(source => source.Sample(now))
                .OrderBy(sample => sample.ResponseKey, StringComparer.Ordinal)
                .ThenBy(sample => sample.Id, StringComparer.Ordinal),
        ];
    }

    public TriggerSample? Find(string id) =>
        List().FirstOrDefault(sample => string.Equals(sample.Id, id, StringComparison.Ordinal));
}
