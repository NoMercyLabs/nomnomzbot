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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Progressive, grant-aware scopes (identity-auth §3.4a). Enabling a feature triggers no OAuth when its
/// required scopes are already on the channel's Twitch connection; otherwise it returns an incremental
/// authorize URL requesting <c>granted ∪ required</c> so the user consents to just the delta.
/// Reconciliation on every token store keeps <c>IntegrationConnection.Scopes</c> truthful — a dropped scope
/// emits <see cref="ScopesDroppedEvent"/> and degrades only the features it backed.
/// </summary>
public sealed class ScopeGrantService : IScopeGrantService
{
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly IScopeNotificationService _scopeNotifications;
    private readonly string _baseUrl;

    public ScopeGrantService(
        IApplicationDbContext db,
        IEventBus eventBus,
        ISystemCredentialsProvider credentials,
        IScopeNotificationService scopeNotifications,
        IConfiguration configuration
    )
    {
        _db = db;
        _eventBus = eventBus;
        _credentials = credentials;
        _scopeNotifications = scopeNotifications;
        _baseUrl = configuration["App:BaseUrl"] ?? "http://localhost:5080";
    }

    public IReadOnlyList<string> RequiredScopesFor(string featureKey) =>
        FeatureScopeMap.RequiredScopesFor(featureKey);

    public async Task<Result<ScopeGrantState>> EnsureFeatureScopesAsync(
        Guid broadcasterId,
        string featureKey,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<string> required = FeatureScopeMap.RequiredScopesFor(featureKey);

        IntegrationConnection? connection = await TwitchConnectionAsync(
            broadcasterId,
            cancellationToken
        );
        HashSet<string> granted = new(connection?.Scopes ?? [], StringComparer.OrdinalIgnoreCase);

        List<string> missing = [.. required.Where(s => !granted.Contains(s))];
        if (missing.Count == 0)
            // RequiredScopesFor(feature) ⊆ connection.Scopes → enable now, ZERO OAuth.
            return Result.Success(new ScopeGrantState(AlreadyGranted: true, null, []));

        // Request the union so the user consents once to the full set (existing scopes are silent).
        IReadOnlyList<string> union =
        [
            .. granted.Union(required, StringComparer.OrdinalIgnoreCase),
        ];
        SystemAppCredentials? app = await _credentials.GetAsync(
            AuthEnums.IntegrationProvider.Twitch,
            cancellationToken
        );
        if (app is null)
            return Result.Failure<ScopeGrantState>(
                "Twitch app credentials are not configured.",
                "TWITCH_NOT_CONFIGURED"
            );

        string url = BuildAuthorizeUrl(app.ClientId, union, baseUrl);
        return Result.Success(new ScopeGrantState(AlreadyGranted: false, url, missing));
    }

    public async Task<Result<IReadOnlyList<string>>> ReconcileGrantedScopesAsync(
        Guid connectionId,
        IReadOnlyList<string> actualScopes,
        CancellationToken cancellationToken = default
    )
    {
        IntegrationConnection? connection = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
        if (connection is null)
            return Result.Failure<IReadOnlyList<string>>("No such connection.", "NOT_FOUND");

        HashSet<string> previous = new(connection.Scopes, StringComparer.OrdinalIgnoreCase);
        HashSet<string> actual = new(actualScopes, StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> dropped = [.. previous.Where(s => !actual.Contains(s))];

        // The connection's Scopes always become the authoritative granted set.
        connection.Scopes = [.. actualScopes];
        await _db.SaveChangesAsync(cancellationToken);

        // A re-grant (or any wider grant) clears every recorded missing-scope gap the new set now satisfies, so
        // the dashboard banner clears and a future loss of the same scope is announced afresh. Runs whether or not
        // anything was dropped — the gap clears purely on what is now granted.
        if (connection.BroadcasterId is Guid broadcasterId)
            await _scopeNotifications.ClearResolvedAsync(
                broadcasterId,
                connection.Scopes,
                cancellationToken
            );

        if (dropped.Count == 0)
            return Result.Success<IReadOnlyList<string>>([]);

        // A removed scope disables exactly the features that needed it (never a blind re-auth).
        IReadOnlyList<string> stillSatisfied = FeatureScopeMap.FeaturesSatisfiedBy(actual);
        IReadOnlyList<string> previouslySatisfied = FeatureScopeMap.FeaturesSatisfiedBy(previous);
        IReadOnlyList<string> disabledFeatures =
        [
            .. previouslySatisfied.Except(stillSatisfied, StringComparer.OrdinalIgnoreCase),
        ];

        await _eventBus.PublishAsync(
            new ScopesDroppedEvent
            {
                BroadcasterId = connection.BroadcasterId ?? Guid.Empty,
                Provider = connection.Provider,
                DroppedScopes = dropped,
                DisabledFeatures = disabledFeatures,
            },
            cancellationToken
        );

        return Result.Success(dropped);
    }

    private async Task<IntegrationConnection?> TwitchConnectionAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    ) =>
        await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == broadcasterId
                    && c.Provider == AuthEnums.IntegrationProvider.Twitch
                    && c.DeletedAt == null,
                cancellationToken
            );

    private string BuildAuthorizeUrl(
        string appClientId,
        IReadOnlyList<string> scopes,
        string? baseUrl
    )
    {
        string publicBaseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? _baseUrl : baseUrl).TrimEnd(
            '/'
        );
        string clientId = Uri.EscapeDataString(appClientId);
        string scope = Uri.EscapeDataString(string.Join(' ', scopes));
        string redirectUri = Uri.EscapeDataString($"{publicBaseUrl}/api/v1/auth/twitch/callback");

        return "https://id.twitch.tv/oauth2/authorize"
            + $"?client_id={clientId}"
            + $"&redirect_uri={redirectUri}"
            + "&response_type=code"
            + $"&scope={scope}";
    }
}
