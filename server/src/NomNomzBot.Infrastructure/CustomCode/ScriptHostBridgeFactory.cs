// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The single construction site for the per-execution <see cref="ScriptHostBridge"/> (custom-code.md §3.1). Both the
/// live <see cref="ScriptRunner"/> and the capture-mode <see cref="ScriptTestRunService"/> obtain their real bridge
/// here, so the fat host-dispatch wiring is declared exactly once.
/// </summary>
public sealed class ScriptHostBridgeFactory(
    IChatProvider chatProvider,
    ICurrencyAccountService currencyService,
    IMusicService musicService,
    IHttpClientFactory httpClientFactory,
    IScriptStorageService storageService,
    ITtsDispatchService ttsDispatch,
    IWidgetService widgetService,
    IWidgetEventNotifier widgetNotifier,
    IRewardService rewardService,
    IViewerAnalyticsService viewerAnalytics,
    ITtsConfigService ttsConfig,
    IScheduledPipelineService scheduledPipelines,
    IApplicationDbContext db,
    ISevenTvUserPaintResolver paintResolver,
    IOwnerActionService ownerActions,
    ITwitchUsersApi twitchUsers,
    IMentionTargetResolver mentionTargets,
    IStreamStatsService streamStats,
    ITemplateResolver? templates = null
) : IScriptHostBridgeFactory
{
    public IScriptHostBridge Create(
        Guid broadcasterId,
        string triggeringUserId,
        ScriptReplyTarget? replyTo
    ) =>
        new ScriptHostBridge(
            broadcasterId,
            triggeringUserId,
            replyTo,
            chatProvider,
            currencyService,
            musicService,
            httpClientFactory,
            storageService,
            ttsDispatch,
            widgetService,
            widgetNotifier,
            rewardService,
            viewerAnalytics,
            ttsConfig,
            scheduledPipelines,
            db,
            paintResolver,
            ownerActions,
            twitchUsers,
            mentionTargets,
            streamStats,
            templates: templates
        );
}
