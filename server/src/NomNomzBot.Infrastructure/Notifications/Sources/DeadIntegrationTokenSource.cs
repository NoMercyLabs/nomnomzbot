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
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Integrations.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Integration connections that can no longer be used until the streamer reconnects them: <c>needs_reauth</c>
/// (the refresh failed past the vault's threshold, Twitch revoked the grant, or the stored token could not be
/// decrypted) and <c>expired</c>. The key embeds the invalidation instant, so a re-invalidation after a fix mints
/// a NEW key an old dismissal cannot hide.
/// </summary>
public sealed class DeadIntegrationTokenSource(IApplicationDbContext db) : IActionRequiredSource
{
    private const string KeyPrefix = "token:";

    private static readonly HashSet<string> DeadStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        AuthEnums.IntegrationStatus.NeedsReauth,
        AuthEnums.IntegrationStatus.Expired,
    };

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [
        nameof(IntegrationNeedsReauthEvent),
        nameof(IntegrationConnectedEvent),
        nameof(IntegrationDisconnectedEvent),
        nameof(IntegrationTokenRefreshedEvent),
    ];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        List<IntegrationConnection> connections = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.BroadcasterId == channelId && c.DeletedAt == null)
            .ToListAsync(cancellationToken);

        List<ActionRequiredItemDto> items = [];
        foreach (IntegrationConnection connection in connections)
        {
            if (!DeadStatuses.Contains(connection.Status))
                continue;

            DateTime invalidatedAt =
                connection.LastErrorAt ?? connection.ConnectedAt ?? connection.CreatedAt;
            string key = $"{KeyPrefix}{connection.Id}:{invalidatedAt.Ticks}";
            if (dismissedKeys.Contains(key))
                continue;

            items.Add(
                new ActionRequiredItemDto(
                    Id: key,
                    Kind: "integration_token_dead",
                    Severity: "critical",
                    TitleKey: "attention_integration_reauth_title",
                    MessageKey: MessageKeyFor(connection),
                    Parameters: new()
                    {
                        ["provider"] = connection.Provider,
                        ["failureCount"] = connection.ConsecutiveFailureCount.ToString(),
                    },
                    DetectedAt: invalidatedAt,
                    DeepLinkRoute: "integrations",
                    SourceUserId: null,
                    SourceUserName: null,
                    Count: 1,
                    QueueItemIds: []
                )
            );
        }

        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    /// <summary>
    /// Expired and refresh-failed connections each say what happened; a <c>needs_reauth</c> with no refresh
    /// failures behind it (a revoked grant, an undecryptable stored token) only says it must be reconnected.
    /// </summary>
    private static string MessageKeyFor(IntegrationConnection connection) =>
        connection.Status == AuthEnums.IntegrationStatus.Expired
            ? "attention_integration_expired_message"
        : connection.ConsecutiveFailureCount > 0 ? "attention_integration_refresh_failed_message"
        : "attention_integration_unusable_message";
}
