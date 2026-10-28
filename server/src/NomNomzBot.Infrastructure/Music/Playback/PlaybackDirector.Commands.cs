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

using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

public sealed partial class PlaybackDirector
{
    /// <summary>
    /// Sends one Pause, Resume, Stop, Seek or Volume command to an engine. An engine that cannot obey
    /// throws a typed block; the director moves to Blocked with that reason and reports false.
    /// </summary>
    private async Task<bool> CommandAsync(
        EngineKind engine,
        string inputName,
        Func<IPlaybackEngine, Task> command
    )
    {
        try
        {
            await command(_engines[engine]);
            return true;
        }
        catch (PlaybackEngineBlockedException blocked)
        {
            await MoveAsync(
                PlaybackState.Blocked(blocked.Reason.ToString()),
                inputName,
                "engine_blocked"
            );
            return false;
        }
    }
}
