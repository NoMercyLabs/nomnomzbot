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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.ViewerData.Builtins;

/// <summary>
/// <c>!stats [@user]</c> (alias <c>!profile</c>) — a viewer's headline stats in chat, composing the
/// EXISTING read-models (per-viewer-data.md D2/D4: analytics M.1 profile + M.3 streak + the economy
/// wallet — no new projection). A channel re-words the reply per slot (commands-pipelines.md §11) and may use
/// <c>{stats.user}</c>, <c>{stats.messages}</c>, <c>{stats.watchtime}</c>, <c>{stats.points}</c>,
/// <c>{stats.rank}</c>, <c>{stats.rankpart}</c>, <c>{stats.streak}</c>, <c>{stats.streakpart}</c>,
/// <c>{stats.firstseen}</c>.
/// </summary>
public abstract class StatsBuiltinBase : IBuiltinCommand
{
    private readonly IViewerAnalyticsService _analytics;
    private readonly ICurrencyAccountService _wallets;
    private readonly IUserService _users;
    private readonly IApplicationDbContext _db;
    private readonly IBuiltinResponseComposer _composer;

    protected StatsBuiltinBase(
        IViewerAnalyticsService analytics,
        ICurrencyAccountService wallets,
        IUserService users,
        IApplicationDbContext db,
        IBuiltinResponseComposer composer
    )
    {
        _analytics = analytics;
        _wallets = wallets;
        _users = users;
        _db = db;
        _composer = composer;
    }

    private const string AccountUnresolvedCode = "ACCOUNT_UNRESOLVED";

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
            return subject.ErrorCode == AccountUnresolvedCode
                ? await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Stats.AccountUnresolved,
                    "stats: your account could not be resolved.",
                    null,
                    ct
                )
                : await NotSeenAsync(context, subject.ErrorDetail!, ct);

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
            return await NotSeenAsync(context, label, ct);

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

        // Precomputed pieces so the shipped line still reads right when the viewer has no rank or streak.
        Dictionary<string, string> vars = new(StringComparer.OrdinalIgnoreCase)
        {
            ["stats.user"] = label,
            ["stats.messages"] = messages.ToString(),
            ["stats.watchtime"] = FormatWatchTime(watchSeconds),
            ["stats.points"] = points.ToString(),
            ["stats.rank"] = rank?.ToString() ?? "unranked",
            ["stats.rankpart"] = rank is not null ? $" (rank #{rank})" : string.Empty,
            ["stats.streak"] = currentStreak.ToString(),
            ["stats.streakpart"] =
                currentStreak > 0 ? $" · {currentStreak}-stream streak" : string.Empty,
            ["stats.firstseen"] = firstSeen,
        };
        return await ReplyAsync(
            context,
            BuiltinResponseSlots.Stats.Profile,
            "{stats.user} · {stats.messages} messages · {stats.watchtime} watched · {stats.points} points{stats.rankpart}{stats.streakpart} · first seen {stats.firstseen}",
            vars,
            ct
        );
    }

    private Task<Result<string>> NotSeenAsync(
        BuiltinCommandContext context,
        string name,
        CancellationToken ct
    ) =>
        ReplyAsync(
            context,
            BuiltinResponseSlots.Stats.NotSeen,
            "I haven't seen {stats.user} chat here yet.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["stats.user"] = name,
            },
            ct
        );

    /// <summary>Every stats reply is a slot of the <c>stats</c> group, shared by <c>!stats</c> and <c>!profile</c>.</summary>
    private async Task<Result<string>> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken ct
    ) =>
        Result.Success(
            await _composer.ComposeAsync(
                context,
                BuiltinResponseSlots.Stats.Key,
                slot,
                neutralFallback,
                variables,
                ct
            )
        );

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
        string mention =
            argParts.Length > 0 ? MentionParser.ParseUserMention(argParts[0]) : string.Empty;

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
                    AccountUnresolvedCode
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
            ? Result.Failure<(Guid, string)>(
                $"I haven't seen {mention} here yet.",
                "NOT_FOUND",
                mention
            )
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
        IBuiltinResponseComposer composer
    )
        : base(analytics, wallets, users, db, composer) { }

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
        IBuiltinResponseComposer composer
    )
        : base(analytics, wallets, users, db, composer) { }

    public override string BuiltinKey => "profile";
}
