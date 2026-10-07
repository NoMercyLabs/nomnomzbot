// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Gives the sr_queue widget the current song-request queue on join: the live <c>sr_queue</c> event with the
/// same <c>{ items }</c> payload. An empty queue is a true state, so it still gives one frame with no items and
/// the widget clears.
/// </summary>
internal sealed class SrQueueSeedProvider(IMusicService music) : IWidgetSeedProvider
{
    public string NaturalKey => "sr_queue";

    public Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult<IReadOnlyList<WidgetSeedFrame>>([
            new WidgetSeedFrame(
                "sr_queue",
                new { items = music.SnapshotQueue(broadcasterId.ToString()) },
                DateTimeOffset.UtcNow
            ),
        ]);
}
