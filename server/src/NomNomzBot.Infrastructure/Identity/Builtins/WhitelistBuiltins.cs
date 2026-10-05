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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.Identity.Builtins;

/// <summary>
/// <c>!whitelist @user subscriber|vip|moderator</c> (broadcaster only): writes a
/// <see cref="StandingSource.Manual"/> community-standing row that the command gate reads and no sync overwrites.
/// Moderator is a Plane-A standing (command level 10), never a <c>PermitGrant</c> role: a permit role would also
/// open Plane-B dashboard powers, which the old bot's whitelist never gave.
/// </summary>
public sealed class WhitelistBuiltin(
    ICommunityStandingService standings,
    IUserService users,
    ITwitchUsersApi twitchUsers,
    IBuiltinResponseComposer composer
) : IBuiltinCommand
{
    private static readonly Dictionary<string, CommunityStanding> Levels = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["subscriber"] = CommunityStanding.Subscriber,
        ["vip"] = CommunityStanding.Vip,
        ["moderator"] = CommunityStanding.Moderator,
    };

    public string BuiltinKey => "whitelist";
    public int DefaultCooldownSeconds => 0;
    public int DefaultMinPermissionLevel => WhitelistSupport.BroadcasterLevel;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        const string verb = BuiltinResponseSlots.Whitelist.Key;
        if (context.RoleLevel < WhitelistSupport.BroadcasterLevel)
            return await WhitelistSupport.ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Whitelist.NotAllowed,
                "Only the broadcaster can use !whitelist.",
                null,
                ct
            );

        string[] args = context.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length < 2)
            return await WhitelistSupport.ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Whitelist.Usage,
                "Usage: !whitelist @user subscriber/vip/moderator",
                null,
                ct
            );

        string levelName = args[1].Trim();
        if (!Levels.TryGetValue(levelName, out CommunityStanding standing))
            return await WhitelistSupport.ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Whitelist.InvalidLevel,
                "Invalid level \"{whitelist.level}\". Valid levels: subscriber, vip, moderator",
                PermitBuiltinSupport.Vars(("whitelist.level", levelName)),
                ct
            );

        Result<(Guid UserId, string Label)> target = await PermitBuiltinSupport.ResolveTargetAsync(
            users,
            twitchUsers,
            args[0],
            verb,
            ct
        );
        if (target.IsFailure)
            return await WhitelistSupport.ReplyToTargetFailureAsync(
                composer,
                context,
                verb,
                target,
                BuiltinResponseSlots.Whitelist.TargetNotFound,
                BuiltinResponseSlots.Whitelist.Failed,
                "The whitelist could not be saved.",
                ct
            );

        Result saved = await standings.SetManualStandingAsync(
            context.BroadcasterId,
            target.Value.UserId,
            standing,
            ct
        );
        return saved.IsFailure
            ? await WhitelistSupport.ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Whitelist.Failed,
                "The whitelist could not be saved.",
                null,
                ct
            )
            : await WhitelistSupport.ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Whitelist.Granted,
                "@{user} has been granted {whitelist.level} level access.",
                PermitBuiltinSupport.Vars(
                    ("user", target.Value.Label),
                    ("whitelist.level", levelName.ToLowerInvariant())
                ),
                ct
            );
    }
}

/// <summary><c>!unwhitelist @user</c> (broadcaster only): removes the manual override, leaving any synced row alone.</summary>
public sealed class UnwhitelistBuiltin(
    ICommunityStandingService standings,
    IUserService users,
    ITwitchUsersApi twitchUsers,
    IBuiltinResponseComposer composer
) : IBuiltinCommand
{
    public string BuiltinKey => "unwhitelist";
    public int DefaultCooldownSeconds => 0;
    public int DefaultMinPermissionLevel => WhitelistSupport.BroadcasterLevel;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        const string verb = BuiltinResponseSlots.Unwhitelist.Key;
        if (context.RoleLevel < WhitelistSupport.BroadcasterLevel)
            return await WhitelistSupport.ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Unwhitelist.NotAllowed,
                "Only the broadcaster can use !unwhitelist.",
                null,
                ct
            );

        string[] args = context.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length < 1)
            return await WhitelistSupport.ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Unwhitelist.Usage,
                "Usage: !unwhitelist @user",
                null,
                ct
            );

        Result<(Guid UserId, string Label)> target = await PermitBuiltinSupport.ResolveTargetAsync(
            users,
            twitchUsers,
            args[0],
            verb,
            ct
        );
        if (target.IsFailure)
            return await WhitelistSupport.ReplyToTargetFailureAsync(
                composer,
                context,
                verb,
                target,
                BuiltinResponseSlots.Unwhitelist.TargetNotFound,
                BuiltinResponseSlots.Unwhitelist.Failed,
                "The whitelist could not be removed.",
                ct
            );

        Result<bool> removed = await standings.RemoveManualStandingAsync(
            context.BroadcasterId,
            target.Value.UserId,
            ct
        );
        Dictionary<string, string> vars = PermitBuiltinSupport.Vars(("user", target.Value.Label));
        if (removed.IsFailure)
            return await WhitelistSupport.ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Unwhitelist.Failed,
                "The whitelist could not be removed.",
                null,
                ct
            );

        return removed.Value
            ? await WhitelistSupport.ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Unwhitelist.Removed,
                "@{user}'s permission override has been removed.",
                vars,
                ct
            )
            : await WhitelistSupport.ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Unwhitelist.NoOverride,
                "@{user} had no permission override.",
                vars,
                ct
            );
    }
}

/// <summary>What <see cref="WhitelistBuiltin"/> and <see cref="UnwhitelistBuiltin"/> share.</summary>
internal static class WhitelistSupport
{
    /// <summary>The Broadcaster rung of the unified ladder.</summary>
    public const int BroadcasterLevel = 40;

    public static async Task<Result<string>> ReplyAsync(
        IBuiltinResponseComposer composer,
        BuiltinCommandContext context,
        string verb,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken ct
    ) =>
        Result.Success(
            await composer.ComposeAsync(context, verb, slot, neutralFallback, variables, ct)
        );

    /// <summary>An unknown Twitch login speaks the "not found" slot; any other lookup failure speaks the failed slot.</summary>
    public static Task<Result<string>> ReplyToTargetFailureAsync(
        IBuiltinResponseComposer composer,
        BuiltinCommandContext context,
        string verb,
        Result failure,
        string notFoundSlot,
        string failedSlot,
        string failedFallback,
        CancellationToken ct
    ) =>
        failure.ErrorCode == PermitBuiltinSupport.TargetNotFoundCode
            ? ReplyAsync(
                composer,
                context,
                verb,
                notFoundSlot,
                "User \"{user}\" not found on Twitch.",
                PermitBuiltinSupport.Vars(("user", failure.ErrorDetail ?? string.Empty)),
                ct
            )
            : ReplyAsync(composer, context, verb, failedSlot, failedFallback, null, ct);
}
