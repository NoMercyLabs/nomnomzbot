// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
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
    IApplicationDbContext db
) : IScriptHostBridgeFactory
{
    public IScriptHostBridge Create(Guid broadcasterId, string triggeringUserId) =>
        new ScriptHostBridge(
            broadcasterId,
            triggeringUserId,
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
            db
        );
}
