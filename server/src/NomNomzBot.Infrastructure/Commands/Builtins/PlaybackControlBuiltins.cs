// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>!pause — pauses the music (mods+, or the music:queue:moderate grant, same as <c>!skip</c>).</summary>
public sealed class PauseBuiltin(
    IMusicService music,
    IBuiltinResponseComposer composer,
    MusicModerationGate gate
) : IBuiltinCommand
{
    public string BuiltinKey => BuiltinResponseSlots.Pause.Key;
    public int DefaultCooldownSeconds => 5;

    // Everyone may TYPE !pause: the moderator-or-grant check runs inside ExecuteAsync (MusicModerationGate),
    // so a non-mod chatter holding the grant is not stopped at the door by the role-ladder floor.
    public int DefaultMinPermissionLevel => 0;

    public Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    ) =>
        PlaybackControlReply.RunAsync(
            context,
            composer,
            gate,
            () => music.PauseAsync(context.BroadcasterId.ToString(), ct),
            BuiltinKey,
            BuiltinResponseSlots.Pause.Paused,
            ct
        );
}

/// <summary>!resume — resumes the music (mods+, or the music:queue:moderate grant, same as <c>!skip</c>).</summary>
public sealed class ResumeBuiltin(
    IMusicService music,
    IBuiltinResponseComposer composer,
    MusicModerationGate gate
) : IBuiltinCommand
{
    public string BuiltinKey => BuiltinResponseSlots.Resume.Key;
    public int DefaultCooldownSeconds => 5;

    // Same door policy as !pause — see PauseBuiltin.DefaultMinPermissionLevel.
    public int DefaultMinPermissionLevel => 0;

    public Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    ) =>
        PlaybackControlReply.RunAsync(
            context,
            composer,
            gate,
            () => music.PlayAsync(context.BroadcasterId.ToString(), ct),
            BuiltinKey,
            BuiltinResponseSlots.Resume.Resumed,
            ct
        );
}

/// <summary>The gate → call → reply flow <see cref="PauseBuiltin"/> and <see cref="ResumeBuiltin"/> share.</summary>
internal static class PlaybackControlReply
{
    public static async Task<Result<string>> RunAsync(
        BuiltinCommandContext context,
        IBuiltinResponseComposer composer,
        MusicModerationGate gate,
        Func<Task<Result>> control,
        string builtinKey,
        string successSlot,
        CancellationToken ct
    )
    {
        if (!await gate.IsAllowedAsync(context, ct))
            return Result.Success(await composer.ComposePermissionDeniedAsync(context, ct));

        Result outcome = await control();
        string slot = outcome.IsSuccess
            ? successSlot
            : BuiltinResponseSlots.PlaybackControl.FailureSlotFor(
                outcome.ErrorCode,
                outcome.ErrorData
            );

        // The service's own sentence stays in logs; chat hears the slot's reply.
        string fallback = ToneTemplateCatalog.Get(PersonalityTone.Informative, builtinKey, slot)[0];
        return Result.Success(
            await composer.ComposeAsync(context, builtinKey, slot, fallback, ct: ct)
        );
    }
}
