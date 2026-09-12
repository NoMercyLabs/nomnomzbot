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
using NomNomzBot.Application.Contracts.Discord;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!discord</c> (legacy parity, S068/§C7 command diff) — replies with a live invite link to the channel's
/// linked Discord server. There is no stored invite URL anywhere in the schema (<c>DiscordGuildConnection</c>
/// carries only the guild id), so this reads the real, both-opt-in-active guild link
/// (<see cref="IDiscordGuildService.GetConnectionsAsync"/>), picks the first channel the bot can actually post
/// in (<see cref="IDiscordBotGateway.GetPostableGuildChannelsAsync"/> — honors per-channel permission
/// overwrites, not just guild-level ones), and creates (or Discord's own de-dupe reuses) a real, permanent
/// invite there (<see cref="IDiscordBotGateway.CreateChannelInviteAsync"/>). Every failure mode — no link, no
/// postable channel, the Discord call itself failing — degrades to an honest "not available" reply; never a
/// fabricated invite.
/// </summary>
public sealed class DiscordInviteBuiltin(
    IDiscordGuildService guilds,
    IDiscordBotGateway gateway,
    IBuiltinResponseComposer composer
) : IBuiltinCommand
{
    public string BuiltinKey => "discord";
    public int DefaultCooldownSeconds => 30;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<DiscordGuildConnectionDto>> connections =
            await guilds.GetConnectionsAsync(context.BroadcasterId, ct);

        DiscordGuildConnectionDto? active = connections.IsSuccess
            ? connections.Value.FirstOrDefault(c => c.IsLinkActive)
            : null;

        if (active is null)
            return Result.Success(
                await ComposeUnavailableAsync(
                    context,
                    BuiltinResponseSlots.Discord.NotConnected,
                    ct
                )
            );

        Result<IReadOnlyList<DiscordPostableChannelDto>> channels =
            await gateway.GetPostableGuildChannelsAsync(context.BroadcasterId, active.GuildId, ct);

        DiscordPostableChannelDto? target = channels.IsSuccess
            ? channels.Value.FirstOrDefault(c => c.CanPost)
            : null;

        if (target is null)
            return Result.Success(
                await ComposeUnavailableAsync(context, BuiltinResponseSlots.Discord.Unavailable, ct)
            );

        Result<string> invite = await gateway.CreateChannelInviteAsync(
            context.BroadcasterId,
            target.Id,
            ct
        );

        if (invite.IsFailure)
            return Result.Success(
                await ComposeUnavailableAsync(context, BuiltinResponseSlots.Discord.Unavailable, ct)
            );

        string message = await composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinResponseSlots.Discord.Key,
                Slot = "invite",
                OverrideTemplate = context.CustomResponseTemplate,
                NeutralFallback = "Join the community on Discord: {discord.invite}",
                Variables = new Dictionary<string, string>
                {
                    ["discord.invite"] = $"discord.gg/{invite.Value}",
                },
            },
            ct
        );
        return Result.Success(message);
    }

    private Task<string> ComposeUnavailableAsync(
        BuiltinCommandContext context,
        string slot,
        CancellationToken ct
    ) =>
        composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinResponseSlots.Discord.Key,
                Slot = slot,
                NeutralFallback =
                    slot == BuiltinResponseSlots.Discord.NotConnected
                        ? "This channel doesn't have a Discord server linked yet."
                        : "The Discord invite isn't available right now — try again in a moment.",
            },
            ct
        );
}
