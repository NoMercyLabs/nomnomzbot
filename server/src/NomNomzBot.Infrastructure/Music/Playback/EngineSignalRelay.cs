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
