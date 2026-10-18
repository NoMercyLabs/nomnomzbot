// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
