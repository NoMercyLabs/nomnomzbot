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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.ViewerData.Builtins;

/// <summary>
/// <c>!stats [@user]</c> (alias <c>!profile</c>) — a viewer's headline stats in chat, composing the
/// EXISTING read-models (per-viewer-data.md D2/D4: analytics M.1 profile + M.3 streak + the economy
/// wallet — no new projection). A custom response template (ChannelBuiltinCommand override) may use
/// <c>{stats.user}</c>, <c>{stats.messages}</c>, <c>{stats.watchtime}</c>, <c>{stats.points}</c>,
/// <c>{stats.rank}</c>, <c>{stats.streak}</c>, <c>{stats.firstseen}</c>.
/// </summary>
public abstract class StatsBuiltinBase : IBuiltinCommand
{
    private readonly IViewerAnalyticsService _analytics;
    private readonly ICurrencyAccountService _wallets;
    private readonly IUserService _users;
    private readonly IApplicationDbContext _db;
    private readonly ITemplateResolver _templates;

    protected StatsBuiltinBase(
        IViewerAnalyticsService analytics,
        ICurrencyAccountService wallets,
        IUserService users,
        IApplicationDbContext db,
        ITemplateResolver templates
    )
    {
        _analytics = analytics;
        _wallets = wallets;
        _users = users;
        _db = db;
        _templates = templates;
    }

    public abstract string BuiltinKey { get; }
    public int DefaultCooldownSeconds => 5;
    public int DefaultMinPermissionLevel => 0; // Everyone — it reads public channel standing.

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        Result<(Guid UserId, string Label)> subject = await ResolveSubjectAsync(context, ct);
        if (subject.IsFailure)
            return Result.Success(subject.ErrorMessage!);

        (Guid viewerId, string label) = subject.Value;

        Result<ViewerProfileDto> profile = await _analytics.GetProfileAsync(
            context.BroadcasterId,
            viewerId,
            ct
        );
        Result<WatchStreakDto> streak = await _analytics.GetStreakAsync(
            context.BroadcasterId,
            viewerId,
            ct
        );
        Result<long> balance = await _wallets.GetBalanceAsync(context.BroadcasterId, viewerId, ct);

        if (profile.IsFailure && balance.IsFailure)
            return Result.Success($"I haven't seen {label} chat here yet.");

        long points = balance.IsSuccess ? balance.Value : 0;
        int? rank = balance.IsSuccess
            ? await ComputeRankAsync(context.BroadcasterId, points, ct)
            : null;
        int currentStreak = streak.IsSuccess ? streak.Value.CurrentStreak : 0;
        long messages = profile.IsSuccess ? profile.Value.TotalMessages : 0;
        long watchSeconds = profile.IsSuccess ? profile.Value.TotalWatchSeconds : 0;
        string firstSeen = profile.IsSuccess
            ? profile.Value.FirstSeenAt?.ToString("yyyy-MM-dd") ?? "unknown"
            : "unknown";

        if (context.CustomResponseTemplate is { Length: > 0 } template)
        {
            Dictionary<string, string> vars = new(StringComparer.OrdinalIgnoreCase)
            {
                ["stats.user"] = label,
                ["stats.messages"] = messages.ToString(),
                ["stats.watchtime"] = FormatWatchTime(watchSeconds),
                ["stats.points"] = points.ToString(),
                ["stats.rank"] = rank?.ToString() ?? "unranked",
                ["stats.streak"] = currentStreak.ToString(),
                ["stats.firstseen"] = firstSeen,
            };
            return Result.Success(_templates.Resolve(template, vars));
        }

        List<string> parts =
        [
            $"{messages} messages",
            $"{FormatWatchTime(watchSeconds)} watched",
            rank is not null ? $"{points} points (rank #{rank})" : $"{points} points",
        ];
        if (currentStreak > 0)
            parts.Add($"{currentStreak}-stream streak");
        parts.Add($"first seen {firstSeen}");

        return Result.Success($"{label} · {string.Join(" · ", parts)}");
    }

    /// <summary>
    /// No arg = the caller (get-or-create); <c>@name</c> = a KNOWN local viewer — stats about someone who
    /// never appeared here are all-zero by definition, so there is deliberately no remote lookup.
    /// </summary>
    private async Task<Result<(Guid, string)>> ResolveSubjectAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        string[] argParts = context.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string mention = argParts.Length > 0 ? argParts[0].TrimStart('@') : string.Empty;

        if (mention.Length == 0)
        {
            Result<UserDto> caller = await _users.GetOrCreateAsync(
                context.TriggeringUserId,
                context.TriggeringUserLogin,
                context.TriggeringUserDisplayName,
                cancellationToken: ct
            );
            if (caller.IsFailure || !Guid.TryParse(caller.Value.Id, out Guid callerId))
                return Result.Failure<(Guid, string)>(
                    "stats: your account could not be resolved.",
                    "NOT_FOUND"
                );
            return Result.Success((callerId, context.TriggeringUserDisplayName));
        }

        string login = mention.ToLowerInvariant();
        var known = await _db
            .Users.AsNoTracking()
            .Where(u => u.Username == login)
            .Select(u => new { u.Id, u.DisplayName })
            .FirstOrDefaultAsync(ct);
        return known is null
            ? Result.Failure<(Guid, string)>($"I haven't seen {mention} here yet.", "NOT_FOUND")
            : Result.Success((known.Id, known.DisplayName ?? mention));
    }

    /// <summary>Dense rank in the channel's single currency: 1 + the number of richer wallets.</summary>
    private async Task<int> ComputeRankAsync(Guid broadcasterId, long balance, CancellationToken ct)
    {
        int richer = await _db.CurrencyAccounts.CountAsync(
            a => a.BroadcasterId == broadcasterId && a.Balance > balance,
            ct
        );
        return richer + 1;
    }

    private static string FormatWatchTime(long totalSeconds)
    {
        long hours = totalSeconds / 3600;
        long minutes = totalSeconds % 3600 / 60;
        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }
}

/// <summary>Chat builtin <c>!stats [@user]</c>.</summary>
public sealed class StatsBuiltin : StatsBuiltinBase
{
    public StatsBuiltin(
        IViewerAnalyticsService analytics,
        ICurrencyAccountService wallets,
        IUserService users,
        IApplicationDbContext db,
        ITemplateResolver templates
    )
        : base(analytics, wallets, users, db, templates) { }

    public override string BuiltinKey => "stats";
}

/// <summary>Chat builtin <c>!profile [@user]</c> — the legacy-parity alias of <c>!stats</c>.</summary>
public sealed class ProfileBuiltin : StatsBuiltinBase
{
    public ProfileBuiltin(
        IViewerAnalyticsService analytics,
        ICurrencyAccountService wallets,
        IUserService users,
        IApplicationDbContext db,
        ITemplateResolver templates
    )
        : base(analytics, wallets, users, db, templates) { }

    public override string BuiltinKey => "profile";
}
