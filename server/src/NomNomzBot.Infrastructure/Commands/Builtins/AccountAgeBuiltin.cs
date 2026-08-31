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
using NomNomzBot.Infrastructure.Identity.Jobs;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!accountage</c> — how long the caller's Twitch account has existed (legacy parity, S068a). Reads
/// the already-hydrated <see cref="User.AccountCreatedAt"/> (set once from Helix Get Users <c>created_at</c>
/// by <see cref="UserProfileHydrationService"/>/login, per-viewer-data §D2); a row that was never hydrated
/// falls back to one live Helix lookup and persists it, the same on-demand-refresh shape
/// <see cref="UpdateUserInfoBuiltin"/> already uses.
/// </summary>
public sealed class AccountAgeBuiltin : IBuiltinCommand
{
    private readonly ITwitchUsersApi _twitchUsers;
    private readonly IUserService _users;
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public AccountAgeBuiltin(
        ITwitchUsersApi twitchUsers,
        IUserService users,
        IApplicationDbContext db,
        TimeProvider clock
    )
    {
        _twitchUsers = twitchUsers;
        _users = users;
        _db = db;
        _clock = clock;
    }

    public string BuiltinKey => "accountage";
    public int DefaultCooldownSeconds => 15;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        await _users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            cancellationToken: ct
        );

        User? row = await _db.Users.FirstOrDefaultAsync(
            u => u.TwitchUserId == context.TriggeringUserId,
            ct
        );
        if (row is null)
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} your account could not be resolved."
            );

        if (row.AccountCreatedAt is null)
        {
            Result<IReadOnlyList<TwitchUser>> lookup = await _twitchUsers.GetUsersByIdsAsync(
                [context.TriggeringUserId],
                ct
            );
            if (lookup.IsFailure)
                return Result.Success(
                    $"@{context.TriggeringUserDisplayName} Twitch did not answer just now — try again in a moment."
                );

            TwitchUser? twitchUser = lookup.Value.FirstOrDefault();
            if (twitchUser is null)
                return Result.Success(
                    $"@{context.TriggeringUserDisplayName} could not find your account on Twitch."
                );

            UserProfileHydrationService.ApplyProfile(row, twitchUser);
            await _db.SaveChangesAsync(ct);
        }

        if (row.AccountCreatedAt is null)
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} your account age could not be determined."
            );

        string age = FormatAge(_clock.GetUtcNow().UtcDateTime - row.AccountCreatedAt.Value);
        return Result.Success(
            $"@{context.TriggeringUserDisplayName} your Twitch account is {age} old."
        );
    }

    private static string FormatAge(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        int totalDays = (int)span.TotalDays;
        int years = totalDays / 365;
        int days = totalDays % 365;
        int months = days / 30;
        days %= 30;

        List<string> parts = [];
        if (years > 0)
            parts.Add($"{years} year{(years == 1 ? "" : "s")}");
        if (months > 0)
            parts.Add($"{months} month{(months == 1 ? "" : "s")}");
        if (years == 0 && months == 0)
            parts.Add($"{days} day{(days == 1 ? "" : "s")}");

        return string.Join(", ", parts);
    }
}
