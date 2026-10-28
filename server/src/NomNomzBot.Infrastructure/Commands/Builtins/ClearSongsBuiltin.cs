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

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!clearsongs &lt;user&gt;</c> — a music moderator (a moderator badge or the <c>music:queue:moderate</c>
/// grant, <see cref="MusicModerationGate"/>) removes every queued song of one requester. It calls the same
/// <see cref="IMusicService.RemoveAllFromRequesterAsync"/> as the dashboard's remove-all action, so the song
/// that is playing now keeps playing.
/// </summary>
public sealed class ClearSongsBuiltin(
    IMusicService music,
    IBuiltinResponseComposer composer,
    MusicModerationGate gate
) : IBuiltinCommand
{
    public string BuiltinKey => BuiltinResponseSlots.ClearSongs.Key;
    public int DefaultCooldownSeconds => 60;

    // Checked in ExecuteAsync through the gate, so a non-mod chatter who holds the grant passes.
    public int DefaultMinPermissionLevel => 0;

    public Task<bool> IsAvailableToAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    ) => gate.IsAllowedAsync(context, ct);

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        if (!await gate.IsAllowedAsync(context, ct))
            return Result.Success(await composer.ComposePermissionDeniedAsync(context, ct));

        string target = context.Args.Trim().TrimStart('@').Trim();
        if (target.Length == 0)
            return Result.Success(
                await ComposeAsync(
                    context,
                    BuiltinResponseSlots.ClearSongs.Usage,
                    "Usage: !clearsongs <user>",
                    new Dictionary<string, string>(),
                    ct
                )
            );

        Dictionary<string, string> variables = new() { ["target"] = target };
        int removed;
        try
        {
            removed = await music.RemoveAllFromRequesterAsync(
                context.BroadcasterId.ToString(),
                target,
                ct
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Success(
                await ComposeAsync(
                    context,
                    BuiltinResponseSlots.ClearSongs.Failed,
                    "Could not remove the songs of {target}. Try again in a moment.",
                    variables,
                    ct
                )
            );
        }

        if (removed == 0)
            return Result.Success(
                await ComposeAsync(
                    context,
                    BuiltinResponseSlots.ClearSongs.NothingQueued,
                    "{target} has no songs in the queue.",
                    variables,
                    ct
                )
            );

        variables["count"] = removed.ToString();
        return Result.Success(
            await ComposeAsync(
                context,
                BuiltinResponseSlots.ClearSongs.Cleared,
                "Removed {count} songs requested by {target}.",
                variables,
                ct
            )
        );
    }

    private Task<string> ComposeAsync(
        BuiltinCommandContext context,
        string slot,
        string fallback,
        Dictionary<string, string> variables,
        CancellationToken ct
    ) => composer.ComposeAsync(context, BuiltinKey, slot, fallback, variables, ct);
}
