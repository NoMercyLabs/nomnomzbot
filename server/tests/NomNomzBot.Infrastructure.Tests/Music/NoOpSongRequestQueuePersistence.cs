// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A no-op <see cref="ISongRequestQueuePersistence"/> for the many <c>MusicService</c>/<c>
/// SongRequestQueueReconciler</c> unit tests that exercise the in-memory fair-queue behavior and have no
/// interest in the S001b durability side-channel — they construct their own focused
/// <c>MusicTestDbContext</c> that does not map <c>SongRequestQueueItems</c>, so a real
/// <c>SongRequestQueuePersistence</c> would throw. The dedicated persistence-behavior tests
/// (<see cref="SongRequestQueuePersistenceTests"/>) use the real implementation against a DbContext that
/// does map the table.
/// </summary>
public sealed class NoOpSongRequestQueuePersistence : ISongRequestQueuePersistence
{
    public Task SyncAsync(
        string broadcasterId,
        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> snapshot,
        CancellationToken cancellationToken,
        SongRequestEntry? inFlight = null
    ) => Task.CompletedTask;

    public Task<SongRequestQueueRestoreResult> LoadForRestoreAsync(
        TimeSpan freshnessWindow,
        CancellationToken cancellationToken
    ) => Task.FromResult(new SongRequestQueueRestoreResult([], []));
}
