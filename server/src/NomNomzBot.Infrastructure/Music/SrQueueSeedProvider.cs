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
