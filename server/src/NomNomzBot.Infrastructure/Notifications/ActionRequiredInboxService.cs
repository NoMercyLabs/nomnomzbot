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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Notifications.Entities;

namespace NomNomzBot.Infrastructure.Notifications;

/// <summary>
/// Aggregates the action-required inbox (S071a, plan item A0) from every registered
/// <see cref="IActionRequiredSource"/>. The inbox owns only what is common to all sources: loading and writing
/// the persisted <see cref="ActionRequiredDismissal"/> rows, routing a dismiss to the source that minted the id,
/// and ordering the result newest first. A source that fails is logged and skipped, so one broken subsystem
/// never blanks the whole inbox.
/// </summary>
public sealed class ActionRequiredInboxService(
    IEnumerable<IActionRequiredSource> sources,
    IApplicationDbContext db,
    TimeProvider clock,
    ILogger<ActionRequiredInboxService> logger
) : IActionRequiredInboxService
{
    private readonly List<IActionRequiredSource> _sources = [.. sources];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        HashSet<string> dismissedKeys = await LoadDismissedKeysAsync(channelId, cancellationToken);
        List<ActionRequiredItemDto> items = [];
        foreach (IActionRequiredSource source in _sources)
        {
            Result<List<ActionRequiredItemDto>> produced = await source.GetItemsAsync(
                channelId,
                dismissedKeys,
                cancellationToken
            );
            if (produced.IsFailure)
            {
                logger.LogWarning(
                    "Action-required source {Source} failed for {ChannelId}: {Error}",
                    source.GetType().Name,
                    channelId,
                    produced.ErrorMessage
                );
                continue;
            }

            items.AddRange(produced.Value);
        }

        return Result.Success(items.OrderByDescending(i => i.DetectedAt).ToList());
    }

    public async Task<Result<int>> DismissAsync(
        Guid channelId,
        Guid dismissedByUserId,
        List<string> ids,
        CancellationToken cancellationToken = default
    )
    {
        List<string> requestedIds =
        [
            .. ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal),
        ];
        if (requestedIds.Count == 0)
            return Result.Failure<int>("No item ids given.", "VALIDATION_FAILED");

        List<string> itemKeys = [];
        foreach (string id in requestedIds)
        {
            IActionRequiredSource? owner = _sources.FirstOrDefault(s =>
                s.KeyPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal))
            );
            if (owner is null)
                return Result.Failure<int>(
                    $"Unknown action-required item id '{id}'.",
                    "VALIDATION_FAILED"
                );

            Result<List<string>> keys = await owner.ResolveDismissalKeysAsync(
                channelId,
                id,
                cancellationToken
            );
            if (keys.IsFailure)
                return keys.ToTyped<int>();
            itemKeys.AddRange(keys.Value);
        }

        return await PersistDismissalsAsync(
            channelId,
            dismissedByUserId,
            [.. itemKeys.Distinct(StringComparer.Ordinal)],
            cancellationToken
        );
    }

    private async Task<Result<int>> PersistDismissalsAsync(
        Guid channelId,
        Guid dismissedByUserId,
        List<string> newKeys,
        CancellationToken cancellationToken
    )
    {
        // Cross-tenant-safe uniqueness check: bypass the ambient tenant filter and re-apply the
        // soft-delete predicate explicitly, matching the filtered unique index (ChannelId, ItemKey).
        List<string> alreadyDismissed = await db
            .ActionRequiredDismissals.IgnoreQueryFilters()
            .Where(d => d.ChannelId == channelId && d.DeletedAt == null)
            .Where(d => newKeys.Contains(d.ItemKey))
            .Select(d => d.ItemKey)
            .ToListAsync(cancellationToken);
        HashSet<string> existingKeys = new(alreadyDismissed, StringComparer.Ordinal);

        DateTime dismissedAt = clock.GetUtcNow().UtcDateTime;
        List<ActionRequiredDismissal> rows =
        [
            .. newKeys
                .Where(key => !existingKeys.Contains(key))
                .Select(key => new ActionRequiredDismissal
                {
                    ChannelId = channelId,
                    ItemKey = key,
                    DismissedByUserId = dismissedByUserId,
                    DismissedAt = dismissedAt,
                }),
        ];
        if (rows.Count == 0)
            return Result.Success(0);

        db.ActionRequiredDismissals.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(rows.Count);
    }

    private async Task<HashSet<string>> LoadDismissedKeysAsync(
        Guid channelId,
        CancellationToken cancellationToken
    )
    {
        List<string> keys = await db
            .ActionRequiredDismissals.IgnoreQueryFilters()
            .Where(d => d.ChannelId == channelId && d.DeletedAt == null)
            .Select(d => d.ItemKey)
            .ToListAsync(cancellationToken);
        return new HashSet<string>(keys, StringComparer.Ordinal);
    }
}
