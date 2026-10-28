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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Rewards;

/// <inheritdoc cref="IViewerInputTemplateService" />
public sealed class ViewerInputTemplateService(
    IFeatureService features,
    IRoleResolver roles,
    IViewerTemplateResolver resolver,
    ITtsConfigService ttsConfig,
    IBillingTierService tiers,
    IApplicationDbContext db,
    IEventBus eventBus,
    ILogger<ViewerInputTemplateService> logger
) : IViewerInputTemplateService
{
    /// <summary>The longest rendered text of a reward input that is not spoken.</summary>
    private const int TextInputCap = 500;

    public async Task<string> ResolveRewardInputAsync(
        Guid broadcasterId,
        ViewerInputIdentity viewer,
        string input,
        bool isTts,
        bool audit,
        CancellationToken cancellationToken = default
    )
    {
        if (!HoldsTemplateSyntax(input))
            return input;

        bool enabled = await features.IsFeatureEnabledAsync(
            broadcasterId.ToString(),
            ViewerTemplateCapabilities.FeatureKey,
            cancellationToken
        );
        if (!enabled)
            return input;

        Guid viewerUserId = await db
            .Users.Where(u => u.TwitchUserId == viewer.TwitchUserId)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!await HoldsActionAsync(viewerUserId, broadcasterId, cancellationToken))
        {
            await RefuseAsync(broadcasterId, viewerUserId, "no_permission", input, audit);
            return input;
        }

        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);
        TemplateSubject.SeedUser(variables, viewer.TwitchUserId, viewer.Login, viewer.DisplayName);
        ViewerTemplateLimits limits = ViewerTemplateLimits.WithOutputCap(
            await OutputCapAsync(broadcasterId, isTts, cancellationToken)
        );
        ViewerTemplateResult result = await resolver.ResolveViewerAsync(
            input,
            variables,
            broadcasterId,
            limits,
            cancellationToken
        );
        if (result.Refusal == ViewerTemplateRefusal.None)
            return result.Text;

        string reason = result.Refusal == ViewerTemplateRefusal.TooDeep ? "too_deep" : "too_long";
        await RefuseAsync(broadcasterId, viewerUserId, reason, input, audit);
        return input;
    }

    /// <summary>A text with no <c>{</c> before a <c>}</c> holds no token, so it skips every lookup.</summary>
    private static bool HoldsTemplateSyntax(string input)
    {
        int open = input.IndexOf('{', StringComparison.Ordinal);
        return open >= 0 && input.IndexOf('}', open) > open;
    }

    /// <summary>Fails closed: an unknown viewer or a failed check means no action.</summary>
    private async Task<bool> HoldsActionAsync(
        Guid viewerUserId,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        if (viewerUserId == Guid.Empty)
            return false;

        Result<bool> held = await roles.HasCapabilityAsync(
            viewerUserId,
            broadcasterId,
            ViewerTemplateCapabilities.RewardInput,
            cancellationToken
        );
        return held is { IsSuccess: true, Value: true };
    }

    private async Task<int> OutputCapAsync(
        Guid broadcasterId,
        bool isTts,
        CancellationToken cancellationToken
    )
    {
        if (!isTts)
            return TextInputCap;

        Result<TtsConfigDto> config = await ttsConfig.GetConfigAsync(
            broadcasterId,
            cancellationToken
        );
        return config.IsSuccess
            ? await TtsCharacterCap.ResolveAsync(
                broadcasterId,
                config.Value,
                tiers,
                cancellationToken
            )
            : TtsCharacterCap.DefaultCharacters;
    }

    private async Task RefuseAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string reason,
        string input,
        bool audit
    )
    {
        logger.LogInformation(
            "Viewer template text left literal in {Channel}: {Reason}",
            broadcasterId,
            reason
        );
        if (!audit)
            return;

        await eventBus.PublishAsync(
            new ViewerTemplateRefusedEvent
            {
                BroadcasterId = broadcasterId,
                CallerUserId = viewerUserId,
                ActionKey = ViewerTemplateCapabilities.RewardInput,
                Reason = reason,
                InputLength = input.Length,
            }
        );
    }
}
