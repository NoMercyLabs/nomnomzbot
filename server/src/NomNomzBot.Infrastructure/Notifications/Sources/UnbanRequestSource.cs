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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Moderation;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Pending Twitch unban requests (appeals), one item per request. Appeals live only on Twitch, so each read asks
/// Twitch as the channel owner, or as the asking moderator when the channel is not onboarded; a failed read (missing scope, Helix down) is returned as a failure and never as an
/// empty list. A request leaves the inbox when Twitch resolves it, since the item is derived from the pending
/// list. The EventSub create and resolve events refresh this source live.
/// </summary>
public sealed class UnbanRequestSource(
    IActingModeratorResolverService actingModerator,
    IModerationService moderation
) : IActionRequiredSource
{
    private const string KeyPrefix = "unban:";
    private const string PendingStatus = "pending";

    public string SourceKey => "unban_requests";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(UnbanRequestCreatedEvent), nameof(UnbanRequestResolvedEvent)];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        // The owner's token on an onboarded channel; the asking moderator's own access on one that is not.
        Result<ActingModerator> acting = await actingModerator.ResolveAsync(
            channelId,
            cancellationToken
        );
        if (acting.IsFailure)
            return acting.WithValue<List<ActionRequiredItemDto>>([]);

        Result<List<UnbanRequestDto>> pending = await moderation.GetUnbanRequestsAsync(
            channelId.ToString(),
            acting.Value.OperatorUserId,
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
            DeepLinkRoute: "people/overview",
            SourceUserId: request.UserId,
            SourceUserName: request.UserName,
            Count: 1,
            QueueItemIds: []
        );

    private static string KeyOf(string requestId) => $"{KeyPrefix}{requestId}";
}
