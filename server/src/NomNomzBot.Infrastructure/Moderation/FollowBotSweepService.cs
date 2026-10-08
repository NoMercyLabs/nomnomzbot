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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>What one sweep did.</summary>
/// <param name="Examined">Accounts in the spike window.</param>
/// <param name="Flagged">Accounts with their own evidence (2+ agreeing indicators, no history).</param>
/// <param name="Blocked">Twitch blocks actually placed.</param>
/// <param name="Failed">Flagged accounts whose block Twitch refused; no row is kept for them.</param>
/// <param name="DryRun">Rows were recorded and no Twitch call was made.</param>
public sealed record FollowBotSweepOutcome(
    int Examined,
    int Flagged,
    int Blocked,
    int Failed,
    bool DryRun
);

/// <summary>
/// Turns a follow-spike window into reversible per-account Twitch blocks (spam-defense.md §L3.1 / SD9).
/// The spike chose the window; this type requires each account to produce its own evidence, writes one
/// <see cref="FollowBotBlock"/> per block under one batch id, and never bans.
/// </summary>
public sealed class FollowBotSweepService
{
    private const string TwitchBlockReason = "spam";
    private const string DefaultAvatarMarker = "user-default-pictures";
    private const int GetUsersBatch = 100;

    private readonly IApplicationDbContext _db;
    private readonly ITwitchUsersApi _twitchUsers;
    private readonly TimeProvider _time;
    private readonly ILogger<FollowBotSweepService> _logger;

    public FollowBotSweepService(
        IApplicationDbContext db,
        ITwitchUsersApi twitchUsers,
        TimeProvider time,
        ILogger<FollowBotSweepService> logger
    )
    {
        _db = db;
        _twitchUsers = twitchUsers;
        _time = time;
        _logger = logger;
    }

    public async Task<FollowBotSweepOutcome> SweepAsync(
        Guid broadcasterId,
        FollowSpikeWindow window,
        SpamDefenseSettings settings,
        CancellationToken ct = default
    )
    {
        bool observing =
            settings.DryRun
            || await SpamObservationWindow.IsActiveAsync(_db, _time, broadcasterId, ct);

        DateTime windowStart = window.Unexamined.Min(f => f.FollowedAt).UtcDateTime;
        List<string> ids = [.. window.Unexamined.Select(f => f.UserId)];
        HashSet<string> withHistory = await HistoryAsync(broadcasterId, ids, windowStart, ct);
        HashSet<string> moderators = await ModeratorsAsync(broadcasterId, ids, ct);
        Dictionary<string, TwitchUser> profiles = await ProfilesAsync(ids, ct);

        List<FollowCandidate> candidates =
        [
            .. window.Unexamined.Select(f =>
                CandidateFor(
                    f,
                    profiles.GetValueOrDefault(f.UserId),
                    withHistory.Contains(f.UserId),
                    TierOf(f, moderators.Contains(f.UserId), settings)
                )
            ),
        ];
        FollowBotBlockBatch batch = FollowBotTrack.Examine(candidates);

        DateTime now = _time.GetUtcNow().UtcDateTime;
        int blocked = 0;
        int failed = 0;
        foreach (FollowBotFinding finding in batch.Findings)
        {
            FollowObservation follow = window.Unexamined.First(f => f.UserId == finding.AccountId);
            if (!observing)
            {
                Result result = await _twitchUsers.BlockUserAsync(
                    broadcasterId,
                    finding.AccountId,
                    reason: TwitchBlockReason,
                    ct: ct
                );
                if (result.IsFailure)
                {
                    failed++;
                    _logger.LogWarning(
                        "Follow-bot block of {Account} in {Channel} failed: {Error}",
                        finding.AccountId,
                        broadcasterId,
                        result.ErrorMessage
                    );
                    continue;
                }
                blocked++;
            }

            _db.FollowBotBlocks.Add(
                new FollowBotBlock
                {
                    BroadcasterId = broadcasterId,
                    BatchId = window.BatchId,
                    SubjectPlatformUserId = finding.AccountId,
                    SubjectUsername = follow.Login,
                    Indicators = string.Join(',', finding.Indicators),
                    BatchExamined = window.WindowSize,
                    BlockedAt = now,
                    WasDryRun = observing,
                }
            );
        }

        await _db.SaveChangesAsync(ct);
        return new FollowBotSweepOutcome(
            batch.Examined,
            batch.Findings.Count,
            blocked,
            failed,
            observing
        );
    }

    // Unknown age or profile is treated as "has a profile": a missing fact must never become evidence.
    private static FollowCandidate CandidateFor(
        FollowObservation follow,
        TwitchUser? profile,
        bool hasHistory,
        SpamTrustTier tier
    ) =>
        new(
            follow.UserId,
            follow.Login,
            profile is null ? double.MaxValue : (follow.FollowedAt - profile.CreatedAt).TotalHours,
            profile is null
                || profile.Description.Length > 0
                || !profile.ProfileImageUrl.Contains(DefaultAvatarMarker, StringComparison.Ordinal),
            FollowUnfollowCycles: 0,
            IsOnKnownBotList: false,
            Tier: tier,
            HasHistory: hasHistory
        );

    // Same ladder the message path uses. A follower has no chat here (history already excludes anyone who
    // has), so standing can only come from a seat in this channel; a moderator resolves above the shield.
    private static SpamTrustTier TierOf(
        FollowObservation follow,
        bool isModeratorHere,
        SpamDefenseSettings settings
    ) =>
        TrustTierLadder.Resolve(
            new AccountFacts
            {
                AccountAgeDays = 0,
                Follow = FollowState.Following,
                FollowAgeHours = 0,
                Username = follow.Login,
            },
            new ChannelParticipation
            {
                IsModeratorHere = isModeratorHere,
                DaysSinceLastUpheldStrike = double.MaxValue,
            },
            new AccountRiskAssessment(1.0, [], IsSemiTrusted: false),
            new TrustTierThresholds
            {
                EstablishedDays = settings.TrustThresholds.EstablishedDays,
                EstablishedMessages = settings.TrustThresholds.EstablishedMessages,
                EstablishedDistinctActiveDays = settings
                    .TrustThresholds
                    .EstablishedDistinctActiveDays,
            }
        );

    /// <summary>
    /// Accounts that are not strangers: any chat message anywhere on this instance, or an earlier follow
    /// of this channel.
    /// </summary>
    private async Task<HashSet<string>> HistoryAsync(
        Guid broadcasterId,
        List<string> ids,
        DateTime windowStart,
        CancellationToken ct
    )
    {
        List<string> chatted = await _db
            .ChatMessages.IgnoreQueryFilters()
            .Where(m => ids.Contains(m.UserId))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);

        List<string?> followedBefore = await _db
            .ChannelEvents.IgnoreQueryFilters()
            .Where(e =>
                e.ChannelId == broadcasterId
                && e.Type == "channel.follow"
                && e.CreatedAt < windowStart
            )
            .Join(_db.Users, e => e.UserId, u => u.Id, (e, u) => u.TwitchUserId)
            .Where(t => ids.Contains(t!))
            .ToListAsync(ct);

        HashSet<string> history = [.. chatted];
        history.UnionWith(followedBefore.OfType<string>());
        return history;
    }

    private async Task<HashSet<string>> ModeratorsAsync(
        Guid broadcasterId,
        List<string> ids,
        CancellationToken ct
    )
    {
        List<string?> moderators = await _db
            .ChannelModerators.IgnoreQueryFilters()
            .Where(m => m.ChannelId == broadcasterId && m.DeletedAt == null)
            .Select(m => m.User.TwitchUserId)
            .Where(t => ids.Contains(t!))
            .ToListAsync(ct);
        return [.. moderators.OfType<string>()];
    }

    private async Task<Dictionary<string, TwitchUser>> ProfilesAsync(
        List<string> ids,
        CancellationToken ct
    )
    {
        Dictionary<string, TwitchUser> profiles = [];
        foreach (string[] chunk in ids.Chunk(GetUsersBatch))
        {
            Result<IReadOnlyList<TwitchUser>> lookup = await _twitchUsers.GetUsersByIdsAsync(
                chunk,
                ct
            );
            if (lookup.IsFailure)
                continue;
            foreach (TwitchUser user in lookup.Value)
                profiles[user.Id] = user;
        }
        return profiles;
    }
}
