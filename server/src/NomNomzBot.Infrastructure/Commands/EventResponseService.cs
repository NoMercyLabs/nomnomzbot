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
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands;

public class EventResponseService : IEventResponseService
{
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly IBillingTierService _tiers;

    public EventResponseService(
        IApplicationDbContext db,
        IEventBus eventBus,
        IBillingTierService tiers
    )
    {
        _db = db;
        _eventBus = eventBus;
        _tiers = tiers;
    }

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

        // A GET performs NO writes (S048b): the catalog top-up seed used to run here as a side effect of
        // reading — moved to EventResponseDefaultsSeeder (a real lifecycle point: full-startup ISeeder pass
        // + immediate seed-on-onboarding), so this method only ever reads.
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

        // IgnoreQueryFilters: a row the operator soft-deleted (DeleteAsync) is invisible to the normal,
        // tenant/soft-delete-filtered query — but upserting that same event type is a DELIBERATE restore
        // (S048b), so it must find and revive that row rather than leave it orphaned while a second,
        // unrelated row gets created for the same natural key.
        EventResponse? entity = await _db
            .EventResponses.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                e => e.BroadcasterId == broadcaster && e.EventType == eventType,
                cancellationToken
            );
        bool isRestore = entity is { DeletedAt: not null };
        if (isRestore)
        {
            entity!.DeletedAt = null;
        }

        // Tier quota (monetization-billing §3.3): `event_responses` caps ENABLED responses, never raw
        // rows — EventResponseDefaultsSeeder seeds a disabled row per catalog event type for every
        // channel, so raw counts are always at catalog size. Gate only a write that ENABLES a currently-
        // disabled (or new) response.
        bool wantsEnabled = request.IsEnabled ?? entity is null; // create default is enabled
        if (wantsEnabled && entity is not { IsEnabled: true })
        {
            Result<long> cap = await _tiers.GetLimitAsync(
                broadcaster,
                "event_responses",
                cancellationToken
            );
            if (cap is { IsSuccess: true, Value: >= 0 })
            {
                int enabled = await _db.EventResponses.CountAsync(
                    e => e.BroadcasterId == broadcaster && e.IsEnabled,
                    cancellationToken
                );
                if (enabled >= cap.Value)
                    return Errors
                        .QuotaExceeded("enabled event responses", cap.Value)
                        .ToTyped<EventResponseDto>();
            }
        }

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
            // Absent leaves the binding unchanged; Guid.Empty clears it; a real id binds that pipeline (the
            // sentinel convention RewardService uses — a null pipelineId is dropped by the client's
            // explicitNulls=false serializer, so "clear" rides the empty sentinel, not a null).
            if (request.PipelineId.HasValue)
                entity.PipelineId =
                    request.PipelineId.Value == Guid.Empty ? null : request.PipelineId.Value;
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
