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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Builds the Twitch scope/connection diagnostics matrix (twitch-helix.md §5) by diffing the channel's
/// login-truthful <see cref="IntegrationConnection.Scopes"/> against the progressive <see cref="FeatureScopeMap"/>
/// — the same registry the grant-aware enable flow uses, so what diagnostics reports and what enabling a
/// feature decides can never disagree. Read-only; no Twitch call. Returns <c>NOT_FOUND</c> when the tenant has
/// no Twitch connection (the dashboard renders that as "not connected").
/// </summary>
public sealed class TwitchScopeDiagnosticsService(IApplicationDbContext db)
    : ITwitchScopeDiagnosticsService
{
    public async Task<Result<TwitchScopeDiagnosticsDto>> GetScopeDiagnosticsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        IntegrationConnection? connection = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == broadcasterId
                    && c.Provider == AuthEnums.IntegrationProvider.Twitch
                    && c.DeletedAt == null,
                ct
            );

        if (connection is null)
        {
            return Result.Failure<TwitchScopeDiagnosticsDto>(
                "This channel has no Twitch connection.",
                "NOT_FOUND"
            );
        }

        HashSet<string> granted = new(connection.Scopes, StringComparer.OrdinalIgnoreCase);

        // One row per (feature, scope): every FeatureScopeMap entry is a progressive, feature-gated scope, so
        // a missing one is "gated by its feature", not an error. Ordered for a stable matrix (UI + tests).
        List<TwitchScopeRequirementDto> requirements =
        [
            .. FeatureScopeMap
                .Features.SelectMany(feature =>
                    feature.Value.Select(scope => new TwitchScopeRequirementDto(
                        Scope: scope,
                        Feature: feature.Key,
                        Granted: granted.Contains(scope),
                        IsProgressive: true,
                        GatedByFeature: feature.Key
                    ))
                )
                .OrderBy(r => r.Feature, StringComparer.Ordinal)
                .ThenBy(r => r.Scope, StringComparer.Ordinal),
        ];

        return Result.Success(
            new TwitchScopeDiagnosticsDto(connection.Status, connection.Scopes, requirements)
        );
    }
}
