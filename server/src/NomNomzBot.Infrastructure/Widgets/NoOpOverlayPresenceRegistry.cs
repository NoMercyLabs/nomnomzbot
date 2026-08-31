// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// A fallback <see cref="IOverlayPresenceRegistry"/> that always reports nothing attached. The real tracker
/// (<c>OverlayPresenceRegistry</c>) lives in <c>NomNomzBot.Api</c> next to the SignalR hub that owns its writes —
/// Infrastructure cannot reference it without inverting the dependency direction. This <c>TryAddSingleton</c>
/// registration exists only so an Infrastructure-only DI container (this project's own composition tests) can
/// resolve <see cref="IOverlayPresenceRegistry"/> at all; the API host's own registration (<c>Program.cs</c>,
/// after <c>AddInfrastructure</c>) always wins in the running application.
/// </summary>
internal sealed class NoOpOverlayPresenceRegistry : IOverlayPresenceRegistry
{
    public bool IsWidgetAttached(Guid broadcasterId, Guid widgetId) => false;
}
