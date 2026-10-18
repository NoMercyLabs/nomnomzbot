// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>Delivers an engine's signals to every subscriber in order, awaiting each one.</summary>
internal sealed class EngineSignalRelay
{
    public event Func<EngineSignal, Task>? Signals;

    public async Task RaiseAsync(EngineSignal signal)
    {
        Func<EngineSignal, Task>? handlers = Signals;
        if (handlers is null)
            return;

        foreach (Delegate handler in handlers.GetInvocationList())
            await ((Func<EngineSignal, Task>)handler)(signal);
    }
}
