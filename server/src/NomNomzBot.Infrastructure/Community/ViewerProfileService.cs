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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Community.Services;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Stream.Entities;

namespace NomNomzBot.Infrastructure.Community;

/// <summary>
/// Assembles <see cref="ViewerProfileSummaryDto"/> by folding the tables/services each group already owns
/// (owner punch list 2026-09-08 §3) — never a second copy of another service's logic. Reads only; every
/// mutating action (setting a TTS voice, granting a permit, editing a shoutout line, …) keeps its own
/// existing endpoint on its own controller.
/// </summary>
public sealed class ViewerProfileService(
    IApplicationDbContext db,
    IViewerAnalyticsService analytics,
    IPermitService permits,
    IQuoteService quotes,
    ITtsConfigService tts
) : IViewerProfileService
{
    private const int RecentQuotesPageSize = 10;

    public async Task<Result<ViewerProfileSummaryDto>> GetProfileAsync(
        Guid broadcasterId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        User? user = await db
            .Users.Include(u => u.Pronoun)
            .Include(u => u.AltPronoun)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Errors.NotFound<ViewerProfileSummaryDto>("User", userId.ToString());

        ViewerIdentityDto identity = await BuildIdentityAsync(broadcasterId, user, ct);

        UserModerationHistory? modHistory = await db.UserModerationHistories.FirstOrDefaultAsync(
            h => h.BroadcasterId == broadcasterId && h.SubjectUserId == userId,
            ct
        );
        UserModerationHistorySummaryDto? historySummary = modHistory is null
            ? null
            : new(
                modHistory.TimeoutCount,
                modHistory.BanCount,
                modHistory.WarningCount,
                modHistory.MessagesDeletedCount,
                modHistory.FirstSeenAt,
                modHistory.LastActionAt,
                modHistory.LastActionType
            );

        UserTrustScore? trustScore = await db.UserTrustScores.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.SubjectUserId == userId,
            ct
        );
        UserTrustSummaryDto? trust = trustScore is null
            ? null
            : new(trustScore.TrustScore, trustScore.HeatScore, trustScore.ComputedAt);

        Result<ViewerProfileDto> activityResult = await analytics.GetProfileAsync(
            broadcasterId,
            userId,
            ct
        );
        Result<WatchStreakDto> streakResult = await analytics.GetStreakAsync(
            broadcasterId,
            userId,
            ct
        );

        ViewerEconomyDto economy = await BuildEconomyAsync(broadcasterId, userId, ct);
        ViewerPermitsDto viewerPermits = await BuildPermitsAsync(broadcasterId, userId, ct);
        ViewerOverridesDto overrides = await BuildOverridesAsync(broadcasterId, user, ct);
        ViewerCommandUsageDto commandUsage = await BuildCommandUsageAsync(
            broadcasterId,
            userId,
            ct
        );

        List<ViewerDatumDto> freeFormData = await db
            .ViewerData.Where(d => d.BroadcasterId == broadcasterId && d.ViewerUserId == userId)
            .OrderBy(d => d.Key)
            .Select(d => new ViewerDatumDto(d.Key, d.Value))
            .ToListAsync(ct);

        Result<PagedList<QuoteDto>> quotesResult = await quotes.ListByUserAsync(
            broadcasterId,
            userId,
            new PaginationParams(1, RecentQuotesPageSize),
            ct
        );
        IReadOnlyList<QuoteDto> recentQuotes = quotesResult.IsSuccess
            ? quotesResult.Value.Items
            : [];
        int totalQuoteCount = quotesResult.IsSuccess ? quotesResult.Value.TotalCount : 0;

        return Result.Success(
            new ViewerProfileSummaryDto(
                identity,
                historySummary,
                trust,
                activityResult.IsSuccess ? activityResult.Value : null,
                streakResult.IsSuccess ? streakResult.Value : null,
                economy,
                viewerPermits,
                overrides,
                commandUsage,
                freeFormData,
                recentQuotes,
                totalQuoteCount
            )
        );
    }

    private async Task<ViewerIdentityDto> BuildIdentityAsync(
        Guid broadcasterId,
        User user,
        CancellationToken ct
    )
    {
        List<LinkedIdentityDto> linked = await db
            .UserIdentities.Where(i => i.UserId == user.Id)
            .OrderByDescending(i => i.IsPrimary)
            .Select(i => new LinkedIdentityDto(
                i.Provider,
                i.ProviderUsername,
                i.ProviderDisplayName,
                i.ProviderAvatarUrl,
                i.IsPrimary
            ))
            .ToListAsync(ct);

        Domain.Identity.Entities.ChannelCommunityStanding? standing =
            await db.ChannelCommunityStandings.FirstOrDefaultAsync(
                s => s.BroadcasterId == broadcasterId && s.UserId == user.Id,
                ct
            );

        Domain.Identity.Entities.ChannelMembership? membership =
            await db.ChannelMemberships.FirstOrDefaultAsync(
                m => m.BroadcasterId == broadcasterId && m.UserId == user.Id,
                ct
            );

        return new ViewerIdentityDto(
            user.Id,
            user.TwitchUserId,
            user.Username,
            user.DisplayName,
            user.ProfileImageUrl,
            user.Pronoun?.Name,
            user.AltPronoun?.Name,
            linked,
            standing?.Standing.ToString() ?? "everyone",
            standing?.SubTier,
            membership?.ManagementRole.ToString(),
            membership?.GrantedAt,
            user.CreatedAt
        );
    }

    private async Task<ViewerEconomyDto> BuildEconomyAsync(
        Guid broadcasterId,
        Guid userId,
        CancellationToken ct
    )
    {
        // Read-only lookup — deliberately NOT ICurrencyAccountService.GetOrCreateAccountAsync, which lazily
        // mints a wallet + seed ledger entry on first read. Simply viewing a profile must never conjure a
        // wallet for a viewer who has never earned currency.
        Domain.Economy.Entities.CurrencyAccount? account =
            await db.CurrencyAccounts.FirstOrDefaultAsync(
                a => a.BroadcasterId == broadcasterId && a.ViewerUserId == userId,
                ct
            );
        CurrencyAccountDto? wallet = account is null
            ? null
            : new CurrencyAccountDto(
                account.Id,
                account.ViewerUserId,
                account.ViewerTwitchUserId,
                "", // display name/avatar are already surfaced via Identity — not re-joined here
                null,
                account.Balance,
                account.LifetimeEarned,
                account.LifetimeSpent,
                account.IsFrozen,
                account.LastActivityAt
            );

        int entryCount = await db.GiveawayEntries.CountAsync(
            e => e.BroadcasterId == broadcasterId && e.ViewerUserId == userId,
            ct
        );
        int winCount = await db.GiveawayWinners.CountAsync(
            w => w.BroadcasterId == broadcasterId && w.ViewerUserId == userId,
            ct
        );
        bool optedOut = await db.LeaderboardOptOuts.AnyAsync(
            o => o.BroadcasterId == broadcasterId && o.ViewerUserId == userId,
            ct
        );

        return new ViewerEconomyDto(wallet, entryCount, winCount, optedOut);
    }

    private async Task<ViewerPermitsDto> BuildPermitsAsync(
        Guid broadcasterId,
        Guid userId,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<PermitGrantDto>> allGrants = await permits.ListActiveGrantsAsync(
            broadcasterId,
            ct
        );
        List<PermitGrantDto> ownGrants = allGrants.IsSuccess
            ? [.. allGrants.Value.Where(g => g.UserId == userId)]
            : [];

        Domain.Economy.Entities.ViewerAgeConsent? consent = await db
            .ViewerAgeConsents.Where(c =>
                c.BroadcasterId == broadcasterId && c.ViewerUserId == userId
            )
            .OrderByDescending(c => c.ConfirmedAt)
            .FirstOrDefaultAsync(ct);

        return new ViewerPermitsDto(ownGrants, consent?.Granted, consent?.ConfirmedAt);
    }

    private async Task<ViewerOverridesDto> BuildOverridesAsync(
        Guid broadcasterId,
        User user,
        CancellationToken ct
    )
    {
        string? shoutout = null;
        string? raid = null;
        if (user.TwitchUserId is not null)
        {
            List<ShoutoutOverride> overrides = await db
                .ShoutoutOverrides.Where(o =>
                    o.BroadcasterId == broadcasterId && o.TargetTwitchUserId == user.TwitchUserId
                )
                .ToListAsync(ct);
            shoutout = overrides
                .FirstOrDefault(o => o.Kind == ShoutoutOverrideKinds.Shoutout)
                ?.MessageTemplate;
            raid = overrides
                .FirstOrDefault(o => o.Kind == ShoutoutOverrideKinds.Raid)
                ?.MessageTemplate;
        }

        UserTtsVoiceDto? voice = null;
        if (user.TwitchUserId is not null)
        {
            Result<UserTtsVoiceDto> voiceResult = await tts.GetUserVoiceAsync(
                broadcasterId,
                user.TwitchUserId,
                ct
            );
            voice = voiceResult.IsSuccess ? voiceResult.Value : null;
        }

        return new ViewerOverridesDto(shoutout, raid, voice);
    }

    private async Task<ViewerCommandUsageDto> BuildCommandUsageAsync(
        Guid broadcasterId,
        Guid userId,
        CancellationToken ct
    )
    {
        IQueryable<Domain.Commands.Entities.CommandUsage> usage = db.CommandUsages.Where(u =>
            u.BroadcasterId == broadcasterId && u.ViewerUserId == userId
        );
        int total = await usage.CountAsync(ct);
        DateTime? lastUsed =
            total == 0 ? null : await usage.MaxAsync(u => (DateTime?)u.CreatedAt, ct);
        return new ViewerCommandUsageDto(total, lastUsed);
    }
}
