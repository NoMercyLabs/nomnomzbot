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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// !update — refreshes a viewer's cached Twitch profile (username/display name) on demand (old-bot parity).
/// Always available as a system built-in (rate-limited via <see cref="DefaultCooldownSeconds"/>, not a
/// deletable per-channel command). Self-service by default; naming another user requires moderator+.
/// </summary>
public sealed class UpdateUserInfoBuiltin : IBuiltinCommand
{
    private const int ModeratorLevel = 10;

    private readonly ITwitchUsersApi _twitchUsers;
    private readonly IUserService _users;

    public UpdateUserInfoBuiltin(ITwitchUsersApi twitchUsers, IUserService users)
    {
        _twitchUsers = twitchUsers;
        _users = users;
    }

    public string BuiltinKey => "update";
    public int DefaultCooldownSeconds => 30;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string requestedLogin = context.Args.Trim().TrimStart('@').ToLowerInvariant();
        bool targetsSomeoneElse =
            requestedLogin.Length > 0
            && !requestedLogin.Equals(
                context.TriggeringUserLogin,
                StringComparison.OrdinalIgnoreCase
            );

        if (targetsSomeoneElse && context.RoleLevel < ModeratorLevel)
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} you can only update your own info, or be a mod to update others."
            );

        string login = targetsSomeoneElse ? requestedLogin : context.TriggeringUserLogin;
        if (string.IsNullOrWhiteSpace(login))
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} could not resolve your Twitch login."
            );

        Result<IReadOnlyList<TwitchUser>> lookup = await _twitchUsers.GetUsersByLoginsAsync(
            [login],
            ct
        );
        TwitchUser? twitchUser = lookup.IsSuccess ? lookup.Value.FirstOrDefault() : null;
        if (twitchUser is null)
            return Result.Success($"Could not find user '{login}' on Twitch.");

        Result<Application.Identity.Dtos.UserDto> refreshed = await _users.GetOrCreateAsync(
            twitchUser.Id,
            twitchUser.Login,
            twitchUser.DisplayName,
            AuthEnums.Platform.Twitch,
            ct
        );
        if (refreshed.IsFailure)
            return Result.Success($"Something went wrong updating {twitchUser.DisplayName}.");

        return Result.Success($"Updated user info for {twitchUser.DisplayName}!");
    }
}
