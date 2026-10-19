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
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Application.Raids;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;

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
    ITtsConfigService tts,
    IFollowStateService follows,
    ISpamDefenseService spam,
    IRaidHistoryReader raidHistory
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

        ViewerRaidsDto raids = await BuildRaidsAsync(broadcasterId, user, ct);

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
                activityResult.IsSuccess
                    ? activityResult.Value with
                    {
                        TotalBitsCheered = await SumCheeredBitsAsync(broadcasterId, user, ct),
                    }
                    : null,
                streakResult.IsSuccess ? streakResult.Value : null,
                economy,
                viewerPermits,
                overrides,
                commandUsage,
                raids,
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
                i.IsPrimary,
                i.LinkedAt,
                i.LastLoginAt
            ))
            .ToListAsync(ct);

        ChannelCommunityStanding? standing = await db.ChannelCommunityStandings.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.UserId == user.Id,
            ct
        );

        ChannelMembership? membership = await db.ChannelMemberships.FirstOrDefaultAsync(
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
            await ResolveFirstSeenAsync(broadcasterId, user, ct),
            user.AccountCreatedAt,
            user.Description,
            user.BroadcasterType,
            user.Type,
            await ResolveChatColorAsync(broadcasterId, user, ct),
            user.NickName,
            user.Timezone,
            user.IsLurking,
            user.PronounManualOverride,
            await ResolveFollowedAtUtcAsync(broadcasterId, user, ct),
            await ResolveTrustTierAsync(broadcasterId, user, standing, ct)
        );
    }

    // The bot's own ladder, never Twitch's account type. Any failure means "could not find out" (null).
    private async Task<string?> ResolveTrustTierAsync(
        Guid broadcasterId,
        User user,
        ChannelCommunityStanding? standing,
        CancellationToken ct
    )
    {
        if (user.TwitchUserId is null)
            return null;

        try
        {
            SpamTrustTier tier = await spam.ResolveTrustTierAsync(
                broadcasterId,
                AuthEnums.Platform.Twitch,
                user.TwitchUserId,
                user.DisplayName,
                standing?.Standing == CommunityStanding.Vip,
                !string.IsNullOrEmpty(standing?.SubTier),
                ct
            );
            return tier.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    // Cached Helix lookup; any failure means "could not find out", never a failed profile read.
    private async Task<DateTime?> ResolveFollowedAtUtcAsync(
        Guid broadcasterId,
        User user,
        CancellationToken ct
    )
    {
        if (user.TwitchUserId is null)
            return null;

        try
        {
            FollowLookup lookup = await follows.ResolveAsync(
                broadcasterId,
                AuthEnums.Platform.Twitch,
                user.TwitchUserId,
                ct
            );
            return lookup.State == FollowState.Following ? lookup.FollowedAt?.UtcDateTime : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    // The person's own color wins; without one, the color the bot assigned them in THIS channel.
    private async Task<string?> ResolveChatColorAsync(
        Guid broadcasterId,
        User user,
        CancellationToken ct
    )
    {
        if (user.Color is not null || user.TwitchUserId is null)
            return user.Color;

        return await db
            .ChatterAssignedColors.Where(c =>
                c.BroadcasterId == broadcasterId
                && c.Provider == AuthEnums.Platform.Twitch
                && c.UserId == user.TwitchUserId
            )
            .Select(c => c.ColorHex)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<long> SumCheeredBitsAsync(
        Guid broadcasterId,
        User user,
        CancellationToken ct
    ) =>
        user.TwitchUserId is null
            ? 0
            : await db
                .ChatMessages.Where(m =>
                    m.BroadcasterId == broadcasterId
                    && m.Provider == AuthEnums.Platform.Twitch
                    && m.UserId == user.TwitchUserId
                )
                .SumAsync(m => (long)(m.BitsAmount ?? 0), ct);

    // User.CreatedAt is when the bot first stored the person (often an import), a cross-channel date. It
    // is only the answer when this channel holds no fact of its own.
    private async Task<DateTime> ResolveFirstSeenAsync(
        Guid broadcasterId,
        User user,
        CancellationToken ct
    )
    {
        DateTime? analyticsFirst = await db
            .ViewerProfiles.Where(p =>
                p.BroadcasterId == broadcasterId && p.ViewerUserId == user.Id
            )
            .Select(p => p.FirstSeenAt)
            .FirstOrDefaultAsync(ct);

        DateTime? firstChat = null;
        if (user.TwitchUserId is not null)
        {
            firstChat = await db
                .ChatMessages.Where(m =>
                    m.BroadcasterId == broadcasterId
                    && m.Provider == AuthEnums.Platform.Twitch
                    && m.UserId == user.TwitchUserId
                )
                .OrderBy(m => m.CreatedAt)
                .Select(m => (DateTime?)m.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        if (analyticsFirst is null)
            return firstChat ?? user.CreatedAt;

        if (firstChat is null)
            return analyticsFirst.Value;

        return analyticsFirst < firstChat ? analyticsFirst.Value : firstChat.Value;
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
        if (user.TwitchUserId is not null)
        {
            shoutout = await db
                .ShoutoutOverrides.Where(o =>
                    o.BroadcasterId == broadcasterId && o.TargetTwitchUserId == user.TwitchUserId
                )
                .Select(o => o.MessageTemplate)
                .FirstOrDefaultAsync(ct);
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

        return new ViewerOverridesDto(shoutout, voice);
    }

    // Raid history is keyed by the remote side's Twitch id; the channel's own id tells the two directions apart.
    private async Task<ViewerRaidsDto> BuildRaidsAsync(
        Guid broadcasterId,
        User user,
        CancellationToken ct
    )
    {
        string? ownTwitchId = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == broadcasterId)
            .Select(c => c.ExternalChannelId)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(ownTwitchId) || user.TwitchUserId is null)
            return new ViewerRaidsDto(0, 0);

        Result<RaidHistory> history = await raidHistory.GetHistoryAsync(
            broadcasterId,
            ownTwitchId,
            ct
        );
        if (!history.IsSuccess)
            return new ViewerRaidsDto(0, 0);

        int raidedUs = history.Value.Incoming.TryGetValue(user.TwitchUserId, out RaidStats? from)
            ? from.Count
            : 0;
        int weRaided = history.Value.Outgoing.TryGetValue(user.TwitchUserId, out RaidStats? to)
            ? to.Count
            : 0;
        return new ViewerRaidsDto(raidedUs, weRaided);
    }

    private async Task<ViewerCommandUsageDto> BuildCommandUsageAsync(
        Guid broadcasterId,
        Guid userId,
        CancellationToken ct
    )
    {
        // Only a run that did its work is "used": the same rule ViewerProfileProjection applies to the
        // TotalCommandsUsed counter in the header, so the header and this list show one number.
        IQueryable<Domain.Commands.Entities.CommandUsage> usage = db.CommandUsages.Where(u =>
            u.BroadcasterId == broadcasterId && u.ViewerUserId == userId && u.WasSuccessful
        );
        int total = await usage.CountAsync(ct);
        DateTime? lastUsed =
            total == 0 ? null : await usage.MaxAsync(u => (DateTime?)u.CreatedAt, ct);
        return new ViewerCommandUsageDto(total, lastUsed);
    }
}
