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

    public bool IsOverlayConnected(Guid broadcasterId) => false;

    public string? GetAudioTarget(Guid broadcasterId) => null;

    public Guid? GetAudioWidgetId(Guid broadcasterId) => null;

    public bool IsAudioSourceConnected(Guid broadcasterId) => false;

    public string? GetMusicTarget(Guid broadcasterId) => null;

    public Guid? GetMusicWidgetId(Guid broadcasterId) => null;

    public bool IsMusicSourceConnected(Guid broadcasterId) => false;
}
