// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
