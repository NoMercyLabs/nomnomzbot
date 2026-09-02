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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The unified moderation review queue (moderation.md J.1) — the AutoMod held-message path. Backed by
/// <see cref="ModerationQueueItem"/>: <see cref="EnqueueHeldMessageAsync"/> is called by the AutoMod event
/// handler on <c>automod.message.hold</c>; a moderator lists the pending queue and resolves each via
/// <see cref="ResolveAsync"/>, which relays through <see cref="ITwitchModerationApi.ManageHeldAutoModMessageAsync"/>
/// before recording the local resolution. <see cref="ApplyExternalResolutionAsync"/> closes a row when Twitch
/// reports the message was resolved outside this dashboard (another moderator, or Twitch auto-expiry).
/// </summary>
public sealed class ModerationQueueService : IModerationQueueService
{
    private static readonly HashSet<string> QueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "approved",
        "denied",
        "actioned",
        "expired",
    };

    private readonly IApplicationDbContext _db;
    private readonly IUserService _users;
    private readonly ITwitchModerationApi _moderation;
    private readonly IModerationService _actions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ModerationQueueService> _logger;

    public ModerationQueueService(
        IApplicationDbContext db,
        IUserService users,
        ITwitchModerationApi moderation,
        IModerationService actions,
        TimeProvider timeProvider,
        ILogger<ModerationQueueService> logger
    )
    {
        _db = db;
        _users = users;
        _moderation = moderation;
        _actions = actions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<Guid>> EnqueueHeldMessageAsync(
        Guid broadcasterId,
        string autoModMessageId,
        string twitchUserId,
        string username,
        string messageContent,
        string category,
        CancellationToken cancellationToken = default
    )
    {
        Guid? targetUserId = null;
        Result<UserDto> user = await _users.GetOrCreateAsync(
            twitchUserId,
            username,
            username,
            cancellationToken: cancellationToken
        );
        if (user.IsSuccess && Guid.TryParse(user.Value.Id, out Guid resolved))
            targetUserId = resolved;

        ModerationQueueItem item = new()
        {
            BroadcasterId = broadcasterId,
            Source = ModerationQueueSource.AutoMod,
            Status = ModerationQueueStatus.Pending,
            TargetUserId = targetUserId,
            TargetTwitchUserId = twitchUserId,
            TargetUsernameSnapshot = username,
            AutoModMessageId = autoModMessageId,
            MessageContentSnapshot = Truncate(messageContent, 500),
            AutoModCategory = category,
        };
        _db.ModerationQueueItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(item.Id);
    }

    public async Task ApplyExternalResolutionAsync(
        Guid broadcasterId,
        string autoModMessageId,
        string twitchStatus,
        CancellationToken cancellationToken = default
    )
    {
        ModerationQueueItem? item = await _db.ModerationQueueItems.FirstOrDefaultAsync(
            i =>
                i.BroadcasterId == broadcasterId
                && i.AutoModMessageId == autoModMessageId
                && i.Status == ModerationQueueStatus.Pending,
            cancellationToken
        );
        if (item is null)
            return;

        item.Status = twitchStatus.Trim().ToLowerInvariant() switch
        {
            "approved" => ModerationQueueStatus.Approved,
            "denied" => ModerationQueueStatus.Denied,
            _ => ModerationQueueStatus.Expired,
        };
        item.ResolutionAction = twitchStatus;
        item.ResolvedAt = _timeProvider.GetUtcNow().UtcDateTime;
        // ResolvedByUserId stays null — this resolution happened outside the dashboard.
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<List<ModerationQueueItemDto>>> ListAsync(
        string broadcasterId,
        string status,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<List<ModerationQueueItemDto>>(broadcasterId);
        if (!QueueStatuses.Contains(status))
            return Result.Failure<List<ModerationQueueItemDto>>(
                $"Unknown queue status '{status}'. Valid: {string.Join(", ", QueueStatuses)}.",
                "VALIDATION_FAILED"
            );

        ModerationQueueStatus parsedStatus = Enum.Parse<ModerationQueueStatus>(
            status,
            ignoreCase: true
        );
        List<ModerationQueueItem> items = await _db
            .ModerationQueueItems.Where(i =>
                i.BroadcasterId == tenantId && i.Status == parsedStatus
            )
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> names = await ResolveNamesAsync(
            items.Select(i => i.ResolvedByUserId),
            cancellationToken
        );
        return Result.Success(items.Select(i => ToDto(i, names)).ToList());
    }

    public async Task<Result<ResolveModerationQueueItemResultDto>> ResolveAsync(
        string broadcasterId,
        Guid queueItemId,
        ResolveModerationQueueItemRequest request,
        string? resolverUserId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.NotFound<ResolveModerationQueueItemResultDto>(
                "Moderation queue item",
                queueItemId.ToString()
            );

        bool? approveParsed = request.Action.Trim().ToLowerInvariant() switch
        {
            "approve" => true,
            "deny" => false,
            _ => null,
        };
        if (approveParsed is not bool approve)
            return Result.Failure<ResolveModerationQueueItemResultDto>(
                "Unknown action. Supported: approve, deny.",
                "VALIDATION_FAILED"
            );

        string followUp = request.FollowUp?.Trim().ToLowerInvariant() ?? "none";
        if (followUp is not ("none" or "timeout" or "ban"))
            return Result.Failure<ResolveModerationQueueItemResultDto>(
                "Unknown follow-up. Supported: none, timeout, ban.",
                "VALIDATION_FAILED"
            );
        if (followUp != "none" && approve)
            return Result.Failure<ResolveModerationQueueItemResultDto>(
                "A follow-up action is only valid with a deny.",
                "VALIDATION_FAILED"
            );
        if (followUp == "timeout")
        {
            int? seconds = request.TimeoutSeconds;
            if (seconds is null || seconds < 1 || seconds > 1209600)
                return Result.Failure<ResolveModerationQueueItemResultDto>(
                    "Timeout length must be between 1 second and 14 days (1209600 seconds).",
                    "VALIDATION_FAILED"
                );
        }
        Guid operatorGuid = Guid.Empty;
        if (followUp != "none" && !Guid.TryParse(resolverUserId, out operatorGuid))
            return Result.Failure<ResolveModerationQueueItemResultDto>(
                "A timeout or ban follow-up requires a signed-in operator.",
                "VALIDATION_FAILED"
            );

        ModerationQueueItem? item = await _db.ModerationQueueItems.FirstOrDefaultAsync(
            i => i.Id == queueItemId && i.BroadcasterId == tenantId,
            cancellationToken
        );
        if (item is null)
            return Errors.NotFound<ResolveModerationQueueItemResultDto>(
                "Moderation queue item",
                queueItemId.ToString()
            );
        if (item.Status != ModerationQueueStatus.Pending)
            return Result.Failure<ResolveModerationQueueItemResultDto>(
                "This item has already been resolved.",
                "VALIDATION_FAILED"
            );
        if (string.IsNullOrEmpty(item.AutoModMessageId))
            return Result.Failure<ResolveModerationQueueItemResultDto>(
                "This queue item has no held message to resolve.",
                "VALIDATION_FAILED"
            );
        if (followUp != "none" && string.IsNullOrEmpty(item.TargetTwitchUserId))
            return Result.Failure<ResolveModerationQueueItemResultDto>(
                "This held message has no sender to act on.",
                "VALIDATION_FAILED"
            );

        Result relay = await _moderation.ManageHeldAutoModMessageAsync(
            tenantId,
            item.AutoModMessageId,
            approve,
            cancellationToken
        );
        if (relay.IsFailure)
        {
            _logger.LogWarning(
                "AutoMod queue resolve failed for item {ItemId}: {Error}",
                item.Id,
                relay.ErrorMessage
            );
            return Result.Failure<ResolveModerationQueueItemResultDto>(
                relay.ErrorMessage!,
                relay.ErrorCode!
            );
        }

        item.Status = approve ? ModerationQueueStatus.Approved : ModerationQueueStatus.Denied;
        item.ResolutionAction = approve ? "approved" : "denied";
        item.ResolvedByUserId = Guid.TryParse(resolverUserId, out Guid resolverGuid)
            ? resolverGuid
            : null;
        item.ResolvedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);

        // The deny is now a fact on Twitch and locally. A follow-up failure from here on must never undo
        // it — the moderator gets the item back as denied plus the follow-up error, and acts again by hand.
        string? followUpError = null;
        if (followUp != "none")
        {
            Result<ModerationActionResult> followUpResult =
                followUp == "ban"
                    ? await _actions.BanAsync(
                        broadcasterId,
                        operatorGuid,
                        item.TargetTwitchUserId!,
                        request.Reason,
                        null,
                        cancellationToken
                    )
                    : await _actions.TimeoutAsync(
                        broadcasterId,
                        operatorGuid,
                        item.TargetTwitchUserId!,
                        request.TimeoutSeconds!.Value,
                        request.Reason,
                        null,
                        cancellationToken
                    );
            if (followUpResult.IsSuccess)
            {
                item.ResolutionAction = followUp == "ban" ? "denied_banned" : "denied_timeout";
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                followUpError = followUpResult.ErrorMessage ?? "The follow-up action failed.";
                _logger.LogWarning(
                    "AutoMod queue follow-up {FollowUp} failed for item {ItemId}: {Error}",
                    followUp,
                    item.Id,
                    followUpError
                );
            }
        }

        Dictionary<Guid, string> names = await ResolveNamesAsync(
            [item.ResolvedByUserId],
            cancellationToken
        );
        return Result.Success(
            new ResolveModerationQueueItemResultDto(ToDto(item, names), followUpError)
        );
    }

    private async Task<Dictionary<Guid, string>> ResolveNamesAsync(
        IEnumerable<Guid?> ids,
        CancellationToken cancellationToken
    )
    {
        List<Guid> distinct = [.. ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct()];
        if (distinct.Count == 0)
            return new();

        return await _db
            .Users.Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static ModerationQueueItemDto ToDto(
        ModerationQueueItem item,
        Dictionary<Guid, string> names
    ) =>
        new(
            item.Id,
            item.Source.ToString().ToLowerInvariant(),
            item.Status.ToString().ToLowerInvariant(),
            item.TargetTwitchUserId,
            item.TargetUsernameSnapshot,
            item.MessageContentSnapshot,
            item.AutoModCategory,
            item.CreatedAt,
            item.ResolvedAt,
            item.ResolvedByUserId is { } resolver ? names.GetValueOrDefault(resolver) : null,
            item.ResolutionAction
        );
}
