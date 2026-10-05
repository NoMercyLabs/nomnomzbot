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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!followage</c> — how long the caller has followed the channel (legacy parity, Followage.cs). Reads the
/// caller's follow record live from Helix. A follower hears the duration, a non-follower hears that they do not
/// follow, and a failed lookup gets an error line — never a fabricated duration. Every reply is its own slot,
/// rendered through <see cref="IBuiltinResponseComposer"/> so the channel can re-word it.
/// </summary>
public sealed class FollowAgeBuiltin : IBuiltinCommand
{
    private readonly ITwitchChannelsApi _channels;
    private readonly IBuiltinResponseComposer _composer;
    private readonly TimeProvider _clock;

    public FollowAgeBuiltin(
        ITwitchChannelsApi channels,
        IBuiltinResponseComposer composer,
        TimeProvider clock
    )
    {
        _channels = channels;
        _composer = composer;
        _clock = clock;
    }

    public string BuiltinKey => BuiltinResponseSlots.FollowAge.Key;
    public int DefaultCooldownSeconds => 15;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        // The chat id of a non-Twitch chatter means nothing to Helix: never send it there.
        if ((context.TriggeringPlatform ?? AuthEnums.Platform.Twitch) != AuthEnums.Platform.Twitch)
            return Result.Success(string.Empty);

        Result<TwitchChannelFollower?> lookup = await _channels.GetChannelFollowerAsync(
            context.BroadcasterId,
            context.TriggeringUserId,
            ct
        );
        if (lookup.IsFailure)
            return Result.Success(
                await ReplyAsync(
                    context,
                    BuiltinResponseSlots.FollowAge.TwitchUnavailable,
                    "Twitch did not answer just now — try again in a moment.",
                    ct
                )
            );

        if (lookup.Value is null)
            return Result.Success(
                await ReplyAsync(
                    context,
                    BuiltinResponseSlots.FollowAge.NotFollowing,
                    "You are not following!",
                    ct
                )
            );

        string age = LegacyAgeText.FollowAge(
            TimeSpan.FromTicks(Math.Max(0, (_clock.GetUtcNow() - lookup.Value.FollowedAt).Ticks))
        );
        return Result.Success(
            await ReplyAsync(
                context,
                BuiltinResponseSlots.FollowAge.Age,
                "You have been following for {age}!",
                ct,
                ("age", age)
            )
        );
    }

    private Task<string> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        string neutralFallback,
        CancellationToken ct,
        params (string Name, string Value)[] extra
    )
    {
        Dictionary<string, string> variables = new()
        {
            ["user"] = context.TriggeringUserDisplayName,
        };
        foreach ((string name, string value) in extra)
            variables[name] = value;

        return _composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = slot,
                NeutralFallback = neutralFallback,
                Variables = variables,
            },
            ct
        );
    }
}
