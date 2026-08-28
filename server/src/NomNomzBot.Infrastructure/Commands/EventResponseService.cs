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
using NomNomzBot.Application.Abstractions.Templating;
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
    private readonly ITemplateHelperValidator _templateHelperValidator;

    public EventResponseService(
        IApplicationDbContext db,
        IEventBus eventBus,
        ITemplateHelperValidator templateHelperValidator
    )
    {
        _db = db;
        _eventBus = eventBus;
        _templateHelperValidator = templateHelperValidator;
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

        // S042 save-time guard: an event response never carries {{args.*}} or any other Command-only
        // helper — checked before the row is found/created so a bad template never persists.
        Result helperOk = _templateHelperValidator.Validate(
            request.Message,
            TemplateHelperContext.EventResponse
        );
        if (helperOk.IsFailure)
            return helperOk.ToTyped<EventResponseDto>();

        // Rows are a fixed, seeded catalogue (EventResponseDefaultsSeeder seeds one disabled row per
        // catalog event type for every channel) — never user-created and never soft-deleted, so this is a
        // plain tenant-filtered lookup; there is no restore path and no per-channel enable cap
        // (S-EVENTRESPONSE-NO-CREATE removed the decorative NEAR_FREE limit on this resource).
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

    public async Task<Result> ResetToDefaultAsync(
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

        // Honest reset (S-EVENTRESPONSE-NO-CREATE): this row is a permanent catalogue entry, never a
        // user-created/deletable one, so this operation never removes it — it puts the row back to the
        // same disabled, no-message shape EventResponseDefaultsSeeder gives a fresh channel. Renamed from
        // the old DeleteAsync, which used to Remove() the row; that made "Delete" a silent, one-way loss
        // once ListAsync stopped top-up-seeding on read (S048b) — the row simply vanished from the
        // dashboard instead of coming back, contradicting the "reset to default" label the UI already used.
        entity.IsEnabled = false;
        entity.ResponseType = "chat_message";
        entity.Message = null;
        entity.PipelineId = null;
        entity.MetadataJson = new Dictionary<string, string>();

        await _db.SaveChangesAsync(cancellationToken);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcaster,
                Domain = "event-responses",
                EntityId = entity.Id.ToString(),
                Action = "reset",
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
