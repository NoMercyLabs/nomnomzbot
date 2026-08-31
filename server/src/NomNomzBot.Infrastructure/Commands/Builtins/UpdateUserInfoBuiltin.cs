// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity.Jobs;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// !update — re-reads a viewer's Twitch profile on demand (old-bot parity): display name, login AND
/// avatar, through the same <see cref="UserProfileHydrationService.ApplyProfile"/> the background refresh
/// uses, so the on-demand path can never drift from the periodic one or leave a stale avatar behind.
/// Always available as a system built-in (rate-limited via <see cref="DefaultCooldownSeconds"/>, not a
/// deletable per-channel command). Self-service by default; naming another user requires moderator+.
/// </summary>
public sealed class UpdateUserInfoBuiltin : IBuiltinCommand
{
    private const int ModeratorLevel = 10;

    private readonly ITwitchUsersApi _twitchUsers;
    private readonly IUserService _users;
    private readonly IApplicationDbContext _db;

    public UpdateUserInfoBuiltin(
        ITwitchUsersApi twitchUsers,
        IUserService users,
        IApplicationDbContext db
    )
    {
        _twitchUsers = twitchUsers;
        _users = users;
        _db = db;
    }

    public string BuiltinKey => "update";
    public int DefaultCooldownSeconds => 30;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string requestedLogin = MentionParser.ParseUserMention(context.Args).ToLowerInvariant();
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
        // A failed CALL and a login that genuinely does not exist are different facts. Reporting a Helix
        // timeout as "no such user" tells the viewer their account is gone when Twitch simply did not answer.
        if (lookup.IsFailure)
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} Twitch did not answer just now — try again in a moment."
            );

        TwitchUser? twitchUser = lookup.Value.FirstOrDefault();
        if (twitchUser is null)
            return Result.Success($"Could not find user '{login}' on Twitch.");

        // GetOrCreate first so a viewer nobody has seen yet still gets a row to refresh.
        Result<Application.Identity.Dtos.UserDto> refreshed = await _users.GetOrCreateAsync(
            twitchUser.Id,
            twitchUser.Login,
            twitchUser.DisplayName,
            AuthEnums.Platform.Twitch,
            ct
        );
        if (refreshed.IsFailure)
            return Result.Success($"Something went wrong updating {twitchUser.DisplayName}.");

        // GetOrCreate only carries the names. The avatar, offline image, broadcaster type, description and
        // the refreshed-at stamp come from the shared apply — which is the whole point of !update to a
        // viewer who just changed their picture and still sees the old one on the overlay.
        User? row = await _db.Users.FirstOrDefaultAsync(u => u.TwitchUserId == twitchUser.Id, ct);
        if (row is not null)
        {
            UserProfileHydrationService.ApplyProfile(row, twitchUser);
            await _db.SaveChangesAsync(ct);
        }

        return Result.Success($"Updated user info for {twitchUser.DisplayName}!");
    }
}
