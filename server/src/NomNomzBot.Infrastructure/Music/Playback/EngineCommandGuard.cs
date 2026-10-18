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
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// Runs one engine command behind the engine's precondition (a Spotify token, an open YouTube player) and gives
/// every failure the same typed shape: a start returns <see cref="EngineStartResult.BlockedBy"/>, a command
/// throws <see cref="PlaybackEngineBlockedException"/>, a provider control returns a blocked
/// <see cref="EngineControlResult"/>. The command is never sent when the precondition is missing.
/// </summary>
internal sealed class EngineCommandGuard(
    EngineKind kind,
    Func<CancellationToken, Task<EngineBlockReason?>> missingPrecondition
)
{
    public async Task<EngineStartResult> StartAsync(
        Func<Task<bool>> start,
        string refusedReason,
        CancellationToken ct
    )
    {
        EngineBlockReason? missing = await missingPrecondition(ct);
        if (missing is not null)
            return EngineStartResult.BlockedBy(missing.Value);

        try
        {
            return await start() ? EngineStartResult.Ok : EngineStartResult.Refused(refusedReason);
        }
        catch (Exception failure) when (EngineBlockMap.Reason(failure) is not null)
        {
            return EngineStartResult.BlockedBy(EngineBlockMap.Reason(failure)!.Value);
        }
    }

    public async Task CommandAsync(Func<Task> command, CancellationToken ct)
    {
        EngineBlockReason? missing = await missingPrecondition(ct);
        if (missing is not null)
            throw new PlaybackEngineBlockedException(kind, missing.Value);

        try
        {
            await command();
        }
        catch (Exception failure) when (EngineBlockMap.Reason(failure) is not null)
        {
            throw new PlaybackEngineBlockedException(kind, EngineBlockMap.Reason(failure)!.Value);
        }
    }

    public async Task<EngineControlResult> ControlAsync(Func<Task> control, CancellationToken ct)
    {
        EngineControlResult<bool> result = await ControlAsync(
            async () =>
            {
                await control();
                return true;
            },
            ct
        );

        return result.Outcome == EngineControlOutcome.Done
            ? EngineControlResult.Done
            : EngineControlResult.BlockedBy(result.Block!.Value);
    }

    public async Task<EngineControlResult<T>> ControlAsync<T>(
        Func<Task<T>> control,
        CancellationToken ct
    )
    {
        EngineBlockReason? missing = await missingPrecondition(ct);
        if (missing is not null)
            return EngineControlResult<T>.BlockedBy(missing.Value);

        try
        {
            return EngineControlResult<T>.DoneWith(await control());
        }
        catch (Exception failure) when (EngineBlockMap.Reason(failure) is not null)
        {
            return EngineControlResult<T>.BlockedBy(EngineBlockMap.Reason(failure)!.Value);
        }
    }
}
