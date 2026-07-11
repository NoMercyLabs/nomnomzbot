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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands;

public class EventResponseService : IEventResponseService
{
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;

    public EventResponseService(IApplicationDbContext db, IEventBus eventBus)
    {
        _db = db;
        _eventBus = eventBus;
    }

    // The canonical set of Twitch events a streamer can configure responses for.
    // Seeded lazily (disabled + no message) when a broadcaster first visits the page.
    private static readonly string[] DefaultEventTypes =
    [
        "channel.follow",
        "channel.subscribe",
        "channel.subscription.gift",
        "channel.subscription.message",
        "channel.cheer",
        "channel.raid",
        "channel.channel_points_custom_reward_redemption.add",
        "stream.online",
        "stream.offline",
        // Engagement triggers (engagement.md §4) — detected from the chat stream, bound like any other
        // event response. Seeded disabled; the detector itself is separately opted in via EngagementConfig.
        "engagement.first_time_chatter",
        "engagement.returning_chatter",
        "engagement.watch_streak",
    ];

    public async Task<Result<PagedList<EventResponseListItem>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PagedList<EventResponseListItem>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        int existingCount = await _db.EventResponses.CountAsync(
            e => e.BroadcasterId == broadcaster,
            cancellationToken
        );

        if (existingCount == 0)
        {
            List<EventResponse> seeds = DefaultEventTypes
                .Select(et => new EventResponse
                {
                    BroadcasterId = broadcaster,
                    EventType = et,
                    IsEnabled = false,
                    ResponseType = "chat_message",
                })
                .ToList();
            _db.EventResponses.AddRange(seeds);
            await _db.SaveChangesAsync(cancellationToken);
        }

        IQueryable<EventResponse> query = _db.EventResponses.Where(e =>
            e.BroadcasterId == broadcaster
        );
        int total = await query.CountAsync(cancellationToken);

        List<EventResponseListItem> items = await query
            .OrderBy(e => e.EventType)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(e => new EventResponseListItem(
                e.Id,
                e.EventType,
                e.IsEnabled,
                e.ResponseType,
                e.UpdatedAt
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<EventResponseListItem>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<EventResponseDto>> GetByEventTypeAsync(
        string broadcasterId,
        string eventType,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<EventResponseDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        EventResponse? entity = await _db.EventResponses.FirstOrDefaultAsync(
            e => e.BroadcasterId == broadcaster && e.EventType == eventType,
            cancellationToken
        );

        if (entity is null)
            return Errors.NotFound<EventResponseDto>("EventResponse", eventType);

        return Result.Success(ToDto(entity));
    }

    public async Task<Result<EventResponseDto>> UpsertAsync(
        string broadcasterId,
        string eventType,
        UpdateEventResponseDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<EventResponseDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        EventResponse? entity = await _db.EventResponses.FirstOrDefaultAsync(
            e => e.BroadcasterId == broadcaster && e.EventType == eventType,
            cancellationToken
        );

        bool isNew = entity is null;
        if (entity is null)
        {
            entity = new()
            {
                BroadcasterId = broadcaster,
                EventType = eventType,
                ResponseType = request.ResponseType ?? "chat_message",
                IsEnabled = request.IsEnabled ?? true,
                Message = request.Message,
                PipelineId = request.PipelineId,
                MetadataJson = request.Metadata ?? new Dictionary<string, string>(),
            };
            _db.EventResponses.Add(entity);
        }
        else
        {
            if (request.IsEnabled.HasValue)
                entity.IsEnabled = request.IsEnabled.Value;
            if (request.ResponseType is not null)
                entity.ResponseType = request.ResponseType;
            if (request.Message is not null)
                entity.Message = request.Message;
            if (request.PipelineId.HasValue)
                entity.PipelineId = request.PipelineId.Value;
            if (request.Metadata is not null)
                entity.MetadataJson = request.Metadata;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcaster,
                Domain = "event-responses",
                EntityId = entity.Id.ToString(),
                Action = isNew ? "created" : "updated",
            },
            cancellationToken
        );

        return Result.Success(ToDto(entity));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        string eventType,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        EventResponse? entity = await _db.EventResponses.FirstOrDefaultAsync(
            e => e.BroadcasterId == broadcaster && e.EventType == eventType,
            cancellationToken
        );

        if (entity is null)
            return Result.Failure($"EventResponse for '{eventType}' was not found.", "NOT_FOUND");

        _db.EventResponses.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcaster,
                Domain = "event-responses",
                EntityId = entity.Id.ToString(),
                Action = "deleted",
            },
            cancellationToken
        );

        return Result.Success();
    }

    private static EventResponseDto ToDto(EventResponse e) =>
        new(
            e.Id,
            e.EventType,
            e.IsEnabled,
            e.ResponseType,
            e.Message,
            e.PipelineId,
            e.MetadataJson,
            e.CreatedAt,
            e.UpdatedAt
        );
}
