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
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;

namespace NomNomzBot.Infrastructure.Notifications;

/// <summary>
/// Aggregates the action-required inbox (S071a) from the two existing signals that genuinely back it today:
/// dead/expired <see cref="IntegrationConnection"/> rows and pending AutoMod-held <see cref="ModerationQueueItem"/>
/// rows. See <see cref="IActionRequiredInboxService"/> for why the other three candidate categories (missing
/// scopes, failed timers, unban requests) are deliberately excluded rather than faked.
/// </summary>
public sealed class ActionRequiredInboxService(IApplicationDbContext db)
    : IActionRequiredInboxService
{
    private static readonly HashSet<string> DeadConnectionStatuses = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        AuthEnums.IntegrationStatus.NeedsReauth,
        AuthEnums.IntegrationStatus.Expired,
    };

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        List<ActionRequiredItemDto> items = [];
        items.AddRange(await BuildDeadConnectionItemsAsync(channelId, cancellationToken));
        items.AddRange(await BuildHeldMessageItemsAsync(channelId, cancellationToken));
        return Result.Success(items.OrderByDescending(i => i.DetectedAt).ToList());
    }

    private async Task<List<ActionRequiredItemDto>> BuildDeadConnectionItemsAsync(
        Guid channelId,
        CancellationToken cancellationToken
    )
    {
        List<IntegrationConnection> deadConnections = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.BroadcasterId == channelId)
            .ToListAsync(cancellationToken);

        return
        [
            .. deadConnections
                .Where(c => DeadConnectionStatuses.Contains(c.Status))
                .Select(c => new ActionRequiredItemDto(
                    Kind: "integration_token_dead",
                    Severity: "critical",
                    Title: $"{c.Provider} connection needs re-authorization",
                    Message: c.Status == AuthEnums.IntegrationStatus.Expired
                        ? $"The {c.Provider} connection's token has expired. Reconnect it to restore the integration."
                        : $"The {c.Provider} connection failed to refresh {c.ConsecutiveFailureCount} time(s) in a row and needs re-authorization.",
                    DetectedAt: c.LastErrorAt ?? c.ConnectedAt ?? c.CreatedAt,
                    DeepLinkRoute: $"/settings/integrations/{c.Provider}"
                )),
        ];
    }

    private async Task<List<ActionRequiredItemDto>> BuildHeldMessageItemsAsync(
        Guid channelId,
        CancellationToken cancellationToken
    )
    {
        List<ModerationQueueItem> pendingHeldMessages = await db
            .ModerationQueueItems.Where(i =>
                i.BroadcasterId == channelId
                && i.Source == ModerationQueueSource.AutoMod
                && i.Status == ModerationQueueStatus.Pending
            )
            .ToListAsync(cancellationToken);

        return
        [
            .. pendingHeldMessages.Select(i => new ActionRequiredItemDto(
                Kind: "held_chat_message",
                Severity: "warning",
                Title: "A chat message is held for review",
                Message: i.TargetUsernameSnapshot is { } username
                    ? $"AutoMod held a message from {username} ({i.AutoModCategory ?? "flagged"}) pending your review."
                    : $"AutoMod held a message ({i.AutoModCategory ?? "flagged"}) pending your review.",
                DetectedAt: i.CreatedAt,
                DeepLinkRoute: "/moderation/queue"
            )),
        ];
    }
}
