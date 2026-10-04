// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Domain.Alerts.Entities;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// Several queries decide "newest" or "oldest" by the Id alone, or by the Id after a shared timestamp. A plain
/// Guid.CreateVersion7 orders only to the millisecond and is random inside it, so rows made in one burst (a
/// multi-winner draw, a code import, one SaveChanges) sort at random. Each entity here must hand out Ids that
/// sort in creation order in the three orders the databases use: the Guid order, the text order (SQLite) and
/// the big-endian byte order (Postgres).
/// </summary>
public sealed class CreationOrderedIdTests
{
    public static TheoryData<string> Entities =>
        new()
        {
            nameof(AlertQueueEntry),
            nameof(ErasureRequest),
            nameof(Giveaway),
            nameof(GiveawayEntry),
            nameof(GiveawayWinner),
            nameof(GiveawayCodePool),
            nameof(GiveawayCode),
            nameof(Channel),
        };

    private static Guid NewId(string entity) =>
        entity switch
        {
            nameof(AlertQueueEntry) => new AlertQueueEntry().Id,
            nameof(ErasureRequest) => new ErasureRequest().Id,
            nameof(Giveaway) => new Giveaway().Id,
            nameof(GiveawayEntry) => new GiveawayEntry().Id,
            nameof(GiveawayWinner) => new GiveawayWinner().Id,
            nameof(GiveawayCodePool) => new GiveawayCodePool().Id,
            nameof(GiveawayCode) => new GiveawayCode().Id,
            nameof(Channel) => new Channel().Id,
            _ => throw new ArgumentOutOfRangeException(nameof(entity), entity, null),
        };

    [Theory]
    [MemberData(nameof(Entities))]
    public void Ids_sort_in_creation_order_even_inside_one_millisecond(string entity)
    {
        List<Guid> created = [];
        for (int i = 0; i < 3000; i++)
            created.Add(NewId(entity));

        created.Order().Should().Equal(created);
        created
            .Select(g => g.ToString())
            .Order(StringComparer.Ordinal)
            .Should()
            .Equal(created.Select(g => g.ToString()));
        List<byte[]> bytes = created.Select(g => g.ToByteArray(bigEndian: true)).ToList();
        for (int i = 1; i < bytes.Count; i++)
            bytes[i - 1].AsSpan().SequenceCompareTo(bytes[i]).Should().BeNegative();
    }
}
