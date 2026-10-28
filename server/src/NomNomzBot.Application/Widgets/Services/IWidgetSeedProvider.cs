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

using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// Rebuilds the state of one gallery widget from its true source (Twitch, our own rows, the caches) when an
/// overlay joins, as frames shaped like the live events. Found by <see cref="NaturalKey"/>. A source with
/// nothing to say gives no frame; a provider never makes a value up.
/// </summary>
public interface IWidgetSeedProvider
{
    /// <summary>The gallery natural key of the widget this provider seeds.</summary>
    string NaturalKey { get; }

    /// <summary>The frames to replay, oldest first.</summary>
    Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    );
}
