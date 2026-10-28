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

using System.Globalization;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Games;

/// <summary>
/// Gives a live-game overlay (<c>raffle</c>, <c>heist</c>) its round back after a reload: the frame that opened the
/// session and the newest frame since, replayed exactly as the engine pushed them. A settled or cancelled round
/// stays seedable only for the widget's own <c>hideAfterMs</c> (the time the widget would still show it). No
/// session of this game gives no frame. One class serves both widgets, keyed by the natural key it is built with,
/// because the frames and the rule are identical and only the game key differs.
/// </summary>
internal sealed class LiveGameSeedProvider(
    string naturalKey,
    LiveGameFrameStore frames,
    TimeProvider clock
) : IWidgetSeedProvider
{
    private const int DefaultHideAfterMs = 12000;

    public string NaturalKey => naturalKey;

    public Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        if (
            !frames.TryGet(broadcasterId, out LiveGameFrameSnapshot? snapshot)
            || snapshot.GameKey != naturalKey
        )
            return Task.FromResult<IReadOnlyList<WidgetSeedFrame>>([]);

        LiveGameFrame newest = snapshot.Latest ?? snapshot.Open;
        if (snapshot.Terminal && clock.GetUtcNow() - newest.PushedAt > HideAfter(widget))
            return Task.FromResult<IReadOnlyList<WidgetSeedFrame>>([]);

        List<WidgetSeedFrame> seed = [ToSeed(snapshot.Open)];
        if (snapshot.Latest is not null)
            seed.Add(ToSeed(snapshot.Latest));
        return Task.FromResult<IReadOnlyList<WidgetSeedFrame>>(seed);
    }

    private static WidgetSeedFrame ToSeed(LiveGameFrame frame) =>
        new(frame.EventType, frame.Payload, frame.PushedAt);

    private static TimeSpan HideAfter(Widget widget)
    {
        if (
            widget.Settings.TryGetValue("hideAfterMs", out object? raw)
            && int.TryParse(
                Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int hideAfterMs
            )
            && hideAfterMs > 0
        )
            return TimeSpan.FromMilliseconds(hideAfterMs);

        return TimeSpan.FromMilliseconds(DefaultHideAfterMs);
    }
}
