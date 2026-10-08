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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Moderation.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Pending Twitch unban requests (appeals), one item per request. Appeals live only on Twitch, so each read asks
/// Twitch as the channel owner; a failed read (missing scope, Helix down) is returned as a failure and never as an
/// empty list. A request leaves the inbox when Twitch resolves it, since the item is derived from the pending
/// list. The EventSub create and resolve events refresh this source live.
/// </summary>
public sealed class UnbanRequestSource(IApplicationDbContext db, IModerationService moderation)
    : IActionRequiredSource
{
    private const string KeyPrefix = "unban:";
    private const string PendingStatus = "pending";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(UnbanRequestCreatedEvent), nameof(UnbanRequestResolvedEvent)];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        Guid? ownerUserId = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == channelId)
            .Select(c => (Guid?)c.OwnerUserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (ownerUserId is null)
            return Errors.ChannelNotFound<List<ActionRequiredItemDto>>(channelId.ToString());

        Result<List<UnbanRequestDto>> pending = await moderation.GetUnbanRequestsAsync(
            channelId.ToString(),
            ownerUserId.Value,
            PendingStatus,
            cancellationToken
        );
        if (pending.IsFailure)
            return pending.WithValue<List<ActionRequiredItemDto>>([]);

        List<ActionRequiredItemDto> items =
        [
            .. pending
                .Value.Where(r => r.Status == PendingStatus && !dismissedKeys.Contains(KeyOf(r.Id)))
                .Select(ToItem),
        ];
        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static ActionRequiredItemDto ToItem(UnbanRequestDto request) =>
        new(
            Id: KeyOf(request.Id),
            Kind: "unban_request",
            Severity: "warning",
            TitleKey: "attention_unban_request_title",
            MessageKey: "attention_unban_request_message",
            Parameters: new() { ["username"] = request.UserName, ["text"] = request.Text },
            DetectedAt: request.CreatedAt,
            DeepLinkRoute: "moderation",
            SourceUserId: request.UserId,
            SourceUserName: request.UserName,
            Count: 1,
            QueueItemIds: []
        );

    private static string KeyOf(string requestId) => $"{KeyPrefix}{requestId}";
}
