// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.DTOs.Twitch;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;

namespace NomNomzBot.Infrastructure.Dashboard;

/// <summary>
/// Composes the dashboard render manifest for a channel from the existing access, feature,
/// integration, and scope surfaces. Access is resolved first (load-bearing self-introspection);
/// every other section is populated ONLY when the caller's resolved <c>HeldActionKeys</c> clear that
/// surface's Gate-2 read floor, so the single aggregate never discloses data an individual endpoint
/// would have withheld from the same caller. Of the entitled sections, features is load-bearing; a
/// failing integration or scope source degrades to an empty section instead of failing the manifest.
/// </summary>
public sealed class RenderManifestService : IRenderManifestService
{
    // Gate-2 read floors of the aggregated surfaces (mirror the [RequireAction] on each controller).
    private const string FeatureReadAction = "feature:read";
    private const string IntegrationReadAction = "integration:read";
    private const string ScopeDiagnosticsReadAction = "twitch:diagnostics:read";

    private readonly IRoleResolver _roles;
    private readonly IFeatureService _features;
    private readonly IIntegrationStatusService _integrations;
    private readonly IScopeNotificationService _scopes;
    private readonly ILogger<RenderManifestService> _logger;

    public RenderManifestService(
        IRoleResolver roles,
        IFeatureService features,
        IIntegrationStatusService integrations,
        IScopeNotificationService scopes,
        ILogger<RenderManifestService> logger
    )
    {
        _roles = roles;
        _features = features;
        _integrations = integrations;
        _scopes = scopes;
        _logger = logger;
    }

    public async Task<Result<RenderManifestDto>> GetManifestAsync(
        Guid userId,
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        // ── Load-bearing: access (self-introspection, always resolved) ───────
        Result<ResolvedAccessDto> access = await _roles.ResolveAccessAsync(
            userId,
            broadcasterId,
            cancellationToken
        );
        if (access.IsFailure)
            return Result<RenderManifestDto>.Failure(
                access.ErrorMessage!,
                access.ErrorCode,
                access.ErrorDetail
            );

        HashSet<string> held = access.Value.HeldActionKeys.ToHashSet(StringComparer.Ordinal);

        // ── Features — load-bearing, but only for a caller who clears its floor ──
        IReadOnlyList<FeatureStatusDto> features = [];
        if (held.Contains(FeatureReadAction))
        {
            Result<List<FeatureStatusDto>> featuresResult = await _features.GetFeaturesAsync(
                broadcasterId.ToString(),
                cancellationToken
            );
            if (featuresResult.IsFailure)
                return Result<RenderManifestDto>.Failure(
                    featuresResult.ErrorMessage!,
                    featuresResult.ErrorCode,
                    featuresResult.ErrorDetail
                );
            features = featuresResult.Value;
        }

        // ── Integrations — best-effort, entitled callers only ────────────────
        IReadOnlyList<ChannelIntegrationDto> integrations = held.Contains(IntegrationReadAction)
            ? await ResolveIntegrationsAsync(broadcasterId, cancellationToken)
            : [];

        // ── Scopes — best-effort, entitled callers only ──────────────────────
        MissingScopesDto scopes = held.Contains(ScopeDiagnosticsReadAction)
            ? await ResolveScopesAsync(broadcasterId, cancellationToken)
            : EmptyScopes();

        return Result.Success(new RenderManifestDto(access.Value, features, integrations, scopes));
    }

    private async Task<IReadOnlyList<ChannelIntegrationDto>> ResolveIntegrationsAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            Result<List<ChannelIntegrationDto>> result = await _integrations.GetStatusesAsync(
                broadcasterId,
                cancellationToken
            );
            if (result.IsSuccess)
                return result.Value;

            _logger.LogWarning(
                "Render manifest: integration section unavailable for channel {ChannelId}: {Error}",
                broadcasterId,
                result.ErrorMessage
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Render manifest: integration section threw for channel {ChannelId}",
                broadcasterId
            );
        }

        return [];
    }

    private async Task<MissingScopesDto> ResolveScopesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            Result<MissingScopesDto> result = await _scopes.GetMissingScopesAsync(
                broadcasterId,
                cancellationToken
            );
            if (result.IsSuccess)
                return result.Value;

            // A NOT_FOUND (no Twitch connection) or other failure is not fatal to the shell — the
            // scope banner simply has nothing to show.
            _logger.LogDebug(
                "Render manifest: scope section unavailable for channel {ChannelId}: {Error}",
                broadcasterId,
                result.ErrorMessage
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Render manifest: scope section threw for channel {ChannelId}",
                broadcasterId
            );
        }

        return EmptyScopes();
    }

    private static MissingScopesDto EmptyScopes() => new("unknown", Array.Empty<MissingScopeDto>());
}
