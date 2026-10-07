// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
