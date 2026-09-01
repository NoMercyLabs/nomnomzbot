// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// One tenant-scoped table the channel-delete preview counts, and the category it was hand-audited into.
/// </summary>
/// <param name="CategoryKey">The curated category this table's rows are reported under.</param>
/// <param name="EntityType">The entity CLR type — the identity a completeness check matches on.</param>
/// <param name="CountAsync">Counts this table's rows for one tenant, from the real database.</param>
public sealed record ChannelBlastRadiusSource(
    string CategoryKey,
    Type EntityType,
    Func<IApplicationDbContext, Guid, CancellationToken, Task<int>> CountAsync
);

/// <summary>
/// The curated map from every tenant-scoped table to the category a streamer would recognise it as.
/// <para>
/// Deleting a channel destroys every row in 116 tenant-scoped tables. Listing 116 table counts is not consent
/// — nobody reads it, and the important lines drown. So membership is decided ONCE, per table, by hand, into
/// six categories the owner of a channel actually thinks in:
/// </para>
/// <list type="bullet">
/// <item><b>Chat</b> — what was said in chat and the per-message activity derived from it.</item>
/// <item><b>Viewers and economy</b> — who the viewers are, their standing, and every balance, ledger entry,
/// purchase and game result denominated in the channel's currency. This is the category with no external
/// backup anywhere: a balance is not recoverable from any platform API.</item>
/// <item><b>Automations</b> — everything the streamer BUILT: commands, pipelines, timers, event responses,
/// scripts, quotes, rewards, giveaways, sound clips, moderation rules.</item>
/// <item><b>Integrations</b> — the connections to other services and the credentials behind them.</item>
/// <item><b>Overlays</b> — widgets, their versions, and the uploaded assets they render.</item>
/// <item><b>Billing</b> — subscription, invoices and metered usage.</item>
/// </list>
/// <para>
/// Everything genuinely outside those six — infrastructure bookkeeping a streamer has never heard of
/// (sessions, crypto keys, projection state, feature-flag overrides, permission overrides, the event journal)
/// — is counted into a seventh <b>other</b> remainder rather than being dropped. The remainder is what makes
/// the total honest: the six named categories never have to pretend to be the whole number.
/// </para>
/// <para>
/// <see cref="All"/> must cover the tenant-scoped tables EXACTLY — no gaps, no duplicates. That is enforced by
/// a test that reflects over <see cref="IApplicationDbContext"/> rather than by review, so a new tenant-scoped
/// table cannot be added without deciding which category its rows die in.
/// </para>
/// </summary>
public static class ChannelBlastRadiusSources
{
    /// <summary>Every tenant-scoped table, each assigned to exactly one curated category.</summary>
    public static IReadOnlyList<ChannelBlastRadiusSource> All { get; } =
    [
        // ── Chat ──
        Of(BlastRadiusCategoryKeys.ChannelChat, db => db.ChatMessages),
        Of(BlastRadiusCategoryKeys.ChannelChat, db => db.ChatPolls),
        Of(BlastRadiusCategoryKeys.ChannelChat, db => db.CommandUsages),
        Of(BlastRadiusCategoryKeys.ChannelChat, db => db.MessageActivityDailies),
        Of(BlastRadiusCategoryKeys.ChannelChat, db => db.ChannelChatterDays),
        Of(BlastRadiusCategoryKeys.ChannelChat, db => db.ChatPollVotes),
        // ── Viewers and economy ──
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ViewerProfiles),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ViewerData),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ViewerReports),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ViewerAgeConsents),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ViewerEngagementStates),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ViewerEngagementDailies),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.WatchSessions),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.WatchStreaks),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ChannelMemberships),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ChannelCommunityStandings),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ChannelModerationStandings),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ChannelSubscriptions),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.UserModerationHistories),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.UserTrustScores),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ModerationQueueItems),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.UserTtsVoices),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.FoundersBadges),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.CurrencyConfigs),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.CurrencyAccounts),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.CurrencyLedgerEntries),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.CatalogPurchases),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.GamePlays),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.GameSessions),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.SavingsJarMemberships),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.LeaderboardOptOuts),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.LeaderboardSnapshots),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.GiveawayEntries),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.GiveawayWinners),
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.Redemptions),
        // ChannelModerator implements ITenantScoped EXPLICITLY over its public ChannelId key; TenantKey
        // resolves the real mapped column, so it needs no special case here.
        Of(BlastRadiusCategoryKeys.ChannelViewers, db => db.ChannelModerators),
        // ── Automations: everything the streamer built ──
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.Commands),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.ChannelBuiltinCommands),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.CommandCooldownStates),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.ChatTriggers),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.Timers),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.EventResponses),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.Pipelines),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.PipelineSteps),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.PipelineStepConditions),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.PipelineTriggers),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.PipelineExecutions),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.ShoutoutOverrides),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.PipelineRunStates),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.ScheduledPipelineTasks),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.CodeScripts),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.CodeScriptVersions),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.SoundClips),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.Quotes),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.PickLists),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.NamedCounters),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.CustomDataSources),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.Rewards),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.RedemptionTimers),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.Giveaways),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.GiveawayCodePools),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.GiveawayCodes),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.CatalogItems),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.EarningRules),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.GameConfigs),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.SavingsJars),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.LeaderboardConfigs),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.EngagementConfigs),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.ChatFilters),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.ModerationEscalationPolicies),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.ModerationEscalationStates),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.SharedBanSettings),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.SharedBanTrustedChannels),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.YouTubeLiveChatBans),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.TtsConfigs),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.TtsLexiconEntries),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.TtsApprovalQueueEntries),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.MediaShareConfigs),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.MediaShareRequests),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.BlockedTracks),
        Of(BlastRadiusCategoryKeys.ChannelAutomations, db => db.Configurations),
        // ── Integrations ──
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.IntegrationConnections),
        // The encrypted OAuth tokens behind those connections — the credentials themselves.
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.IntegrationTokens),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.Services),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.DiscordGuildConnections),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.DiscordNotificationConfigs),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.DiscordNotificationRoles),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.DiscordMemberOptIns),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.DiscordLiveRoleConfigs),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.DiscordNotificationDispatches),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.ObsConnections),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.VtsConnections),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.SupporterConnections),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.SupporterEvents),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.OutboundWebhookEndpoints),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.OutboundWebhookDeliveries),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.InboundWebhookEndpoints),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.HttpEgressAllowlists),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.AutomationApiTokens),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.EventSubSubscriptions),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.ChannelFederationOptIns),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.InstalledBundles),
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.ChannelBotAuthorizations),
        // The channel's own platform presences (Twitch/Kick/YouTube/X connections) — same category as its
        // bot authorization, not "integrations" in the third-party-service sense but the closest curated home.
        Of(BlastRadiusCategoryKeys.ChannelIntegrations, db => db.PlatformConnections),
        // ── Overlays ──
        Of(BlastRadiusCategoryKeys.ChannelOverlays, db => db.Widgets),
        Of(BlastRadiusCategoryKeys.ChannelOverlays, db => db.WidgetVersions),
        Of(BlastRadiusCategoryKeys.ChannelOverlays, db => db.ChannelAssets),
        Of(BlastRadiusCategoryKeys.ChannelOverlays, db => db.RenderedAlertCaptures),
        // ── Billing ──
        Of(BlastRadiusCategoryKeys.ChannelBilling, db => db.Subscriptions),
        Of(BlastRadiusCategoryKeys.ChannelBilling, db => db.Invoices),
        Of(BlastRadiusCategoryKeys.ChannelBilling, db => db.UsageRecords),
        Of(BlastRadiusCategoryKeys.ChannelBilling, db => db.TtsUsageRecords),
        // ── The remainder: infrastructure bookkeeping with no streamer-facing name ──
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.AuthSessions),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.CryptoKeys),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.ConsentRecords),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.Permissions),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.PermitGrants),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.ChannelActionOverrides),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.ChannelFeatures),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.ChannelMissingScopes),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.ChannelAnalyticsDailies),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.FeatureFlagOverrides),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.SecurityNotices),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.EventJournals),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.Records),
        // Past stream sessions and the tenant's id sequence: real rows that die with the channel, but nothing
        // a streamer would look for under one of the six names. The remainder is where they belong.
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.Streams),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.TenantSequences),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.ChannelEvents),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.ComplianceAuditLogs),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.ErasureRequests),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.EventSubjectKeys),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.IdempotencyKeys),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.KeyUsageBindings),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.ProjectionCheckpoints),
        Of(BlastRadiusCategoryKeys.ChannelOther, db => db.Storages),
    ];

    private static ChannelBlastRadiusSource Of<TEntity>(
        string categoryKey,
        Func<IApplicationDbContext, DbSet<TEntity>> set
    )
        where TEntity : class =>
        new(
            categoryKey,
            typeof(TEntity),
            (db, broadcasterId, ct) =>
                set(db).CountAsync(TenantPredicate<TEntity>(broadcasterId), ct)
        );

    // Built by reflection rather than as a `e => e.BroadcasterId == id` lambda: a lambda over the generic
    // parameter would compile to the ITenantScoped interface member, which EF cannot translate to a column.
    private static Expression<Func<TEntity, bool>> TenantPredicate<TEntity>(Guid broadcasterId)
    {
        PropertyInfo property =
            TenantKey.ResolveProperty(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"{typeof(TEntity).Name} has no mapped tenant column; it cannot be counted per channel."
            );

        ParameterExpression parameter = Expression.Parameter(typeof(TEntity), "e");
        // A nullable tenant column compares against a nullable constant, so a platform-global row (tenant
        // null) never matches any channel — it is not this channel's to destroy.
        ConstantExpression value =
            property.PropertyType == typeof(Guid?)
                ? Expression.Constant(broadcasterId, typeof(Guid?))
                : Expression.Constant(broadcasterId);

        return Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(Expression.Property(parameter, property), value),
            parameter
        );
    }
}
