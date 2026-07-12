// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.PickLists;

/// <summary>
/// The generic named pick-list service. Writes are single-table (no sequence, no transaction) so they save
/// directly through the DbContext; every query goes through <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters"/>
/// with an explicit <c>BroadcasterId</c> + <c>DeletedAt == null</c> predicate, so an operator acting on a channel
/// they moderate is never blinded by the global tenant filter (unique-index-audit) — the scoping is explicit.
/// </summary>
public sealed partial class PickListService : IPickListService
{
    /// <summary>The <see cref="ChannelConfigChangedEvent.Domain"/> tag for this config surface (dashboard live-sync).</summary>
    private const string ConfigDomain = "picklists";

    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 500;
    private const int MaxItems = 500;
    private const int MaxItemLength = 500;

    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;

    public PickListService(IApplicationDbContext db, IEventBus eventBus)
    {
        _db = db;
        _eventBus = eventBus;
    }

    public async Task<Result<PagedList<PickListDto>>> ListAsync(
        Guid broadcasterId,
        PickListSearch search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<PickList> query = _db
            .PickLists.IgnoreQueryFilters()
            .Where(p => p.BroadcasterId == broadcasterId && p.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(search.Term))
        {
            string term = search.Term.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{term}%")
                || (p.Description != null && EF.Functions.Like(p.Description, $"%{term}%"))
            );
        }

        int total = await query.CountAsync(ct);

        // Materialise the page before mapping: Items is a JSON collection, so the DTO projection runs in memory.
        List<PickList> rows = await query
            .OrderBy(p => p.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<PickListDto> items = rows.Select(ToDto).ToList();
        return Result.Success(
            new PagedList<PickListDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<PickListDto>> GetAsync(
        Guid broadcasterId,
        Guid id,
        CancellationToken ct = default
    )
    {
        PickList? list = await _db
            .PickLists.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId && p.Id == id && p.DeletedAt == null,
                ct
            );

        if (list is null)
            return Errors.NotFound<PickListDto>("Pick list", id.ToString());

        return Result.Success(ToDto(list));
    }

    public async Task<Result<PickListDto>> GetByNameAsync(
        Guid broadcasterId,
        string name,
        CancellationToken ct = default
    )
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result.Failure<PickListDto>(
                "A pick list name is required.",
                "VALIDATION_FAILED"
            );

        PickList? list = await _db
            .PickLists.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId && p.Name == trimmed && p.DeletedAt == null,
                ct
            );

        if (list is null)
            return Errors.NotFound<PickListDto>("Pick list", trimmed);

        return Result.Success(ToDto(list));
    }

    public async Task<Result<PickListDto>> CreateAsync(
        Guid broadcasterId,
        CreatePickListRequest request,
        CancellationToken ct = default
    )
    {
        Result<ValidatedInput> validated = Validate(
            request.Name,
            request.Description,
            request.Items
        );
        if (validated.IsFailure)
            return Result.Failure<PickListDto>(validated.ErrorMessage, validated.ErrorCode);
        ValidatedInput input = validated.Value;

        bool channelExists = await _db.Channels.AnyAsync(c => c.Id == broadcasterId, ct);
        if (!channelExists)
            return Errors.ChannelNotFound<PickListDto>(broadcasterId.ToString());

        // Bypass the tenant filter and scope explicitly, catching a soft-deleted namesake so it is revived rather
        // than orphaned behind the DeletedAt-filtered unique index (mirrors SupporterConnectionService).
        PickList? existing = await _db
            .PickLists.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.BroadcasterId == broadcasterId && p.Name == input.Name, ct);

        if (existing is not null && existing.DeletedAt is null)
            return Result.Failure<PickListDto>(
                $"A pick list named '{input.Name}' already exists.",
                "ALREADY_EXISTS"
            );

        PickList list;
        if (existing is not null)
        {
            existing.DeletedAt = null;
            existing.Description = input.Description;
            existing.Items = input.Items;
            list = existing;
        }
        else
        {
            list = new PickList
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                Name = input.Name,
                Description = input.Description,
                Items = input.Items,
            };
            _db.PickLists.Add(list);
        }

        await _db.SaveChangesAsync(ct);
        await PublishChangedAsync(broadcasterId, list.Id, "created", ct);

        return Result.Success(ToDto(list));
    }

    public async Task<Result<PickListDto>> UpdateAsync(
        Guid broadcasterId,
        Guid id,
        UpdatePickListRequest request,
        CancellationToken ct = default
    )
    {
        Result<ValidatedInput> validated = Validate(
            request.Name,
            request.Description,
            request.Items
        );
        if (validated.IsFailure)
            return Result.Failure<PickListDto>(validated.ErrorMessage, validated.ErrorCode);
        ValidatedInput input = validated.Value;

        PickList? list = await _db
            .PickLists.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId && p.Id == id && p.DeletedAt == null,
                ct
            );

        if (list is null)
            return Errors.NotFound<PickListDto>("Pick list", id.ToString());

        // A rename must not collide with another live list on this channel.
        if (!string.Equals(list.Name, input.Name, StringComparison.Ordinal))
        {
            bool nameTaken = await _db
                .PickLists.IgnoreQueryFilters()
                .AnyAsync(
                    p =>
                        p.BroadcasterId == broadcasterId
                        && p.Name == input.Name
                        && p.DeletedAt == null
                        && p.Id != id,
                    ct
                );
            if (nameTaken)
                return Result.Failure<PickListDto>(
                    $"A pick list named '{input.Name}' already exists.",
                    "ALREADY_EXISTS"
                );
        }

        list.Name = input.Name;
        list.Description = input.Description;
        list.Items = input.Items;

        await _db.SaveChangesAsync(ct);
        await PublishChangedAsync(broadcasterId, list.Id, "updated", ct);

        return Result.Success(ToDto(list));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid id,
        CancellationToken ct = default
    )
    {
        PickList? list = await _db
            .PickLists.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId && p.Id == id && p.DeletedAt == null,
                ct
            );

        if (list is null)
            return Errors.NotFound<PickListDto>("Pick list", id.ToString());

        // Soft-delete via the SoftDeleteInterceptor; the row survives with DeletedAt stamped and its name freed.
        _db.PickLists.Remove(list);
        await _db.SaveChangesAsync(ct);
        await PublishChangedAsync(broadcasterId, list.Id, "deleted", ct);

        return Result.Success();
    }

    public async Task<Result<string>> PickRandomAsync(
        Guid broadcasterId,
        string name,
        CancellationToken ct = default
    )
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result.Failure<string>("A pick list name is required.", "VALIDATION_FAILED");

        PickList? list = await _db
            .PickLists.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId && p.Name == trimmed && p.DeletedAt == null,
                ct
            );

        if (list is null)
            return Errors.NotFound<string>("Pick list", trimmed);

        if (list.Items.Count == 0)
            return Result.Failure<string>($"Pick list '{trimmed}' is empty.", "PICKLIST_EMPTY");

        return Result.Success(list.Items[Random.Shared.Next(list.Items.Count)]);
    }

    private Task PublishChangedAsync(
        Guid broadcasterId,
        Guid id,
        string action,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = ConfigDomain,
                EntityId = id.ToString(),
                Action = action,
            },
            ct
        );

    private static PickListDto ToDto(PickList p) =>
        new(p.Id, p.Name, p.Description, p.Items.ToList(), p.CreatedAt, p.UpdatedAt);

    /// <summary>Normalises and range-checks a create/update payload before it touches the database.</summary>
    private static Result<ValidatedInput> Validate(
        string? name,
        string? description,
        List<string>? items
    )
    {
        string trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
            return Result.Failure<ValidatedInput>(
                "A pick list name is required.",
                "VALIDATION_FAILED"
            );
        if (trimmedName.Length > MaxNameLength)
            return Result.Failure<ValidatedInput>(
                $"Pick list name cannot exceed {MaxNameLength} characters.",
                "VALIDATION_FAILED"
            );
        // The name is used verbatim as the {list.pick.<name>} template key, so it is restricted to a clean slug
        // (no braces/spaces that would make the placeholder ambiguous).
        if (!NamePattern().IsMatch(trimmedName))
            return Result.Failure<ValidatedInput>(
                "A pick list name may contain only letters, numbers, underscores, and hyphens.",
                "VALIDATION_FAILED"
            );

        string? trimmedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
        if (trimmedDescription is { Length: > MaxDescriptionLength })
            return Result.Failure<ValidatedInput>(
                $"Pick list description cannot exceed {MaxDescriptionLength} characters.",
                "VALIDATION_FAILED"
            );

        List<string> cleaned =
            items?.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList() ?? [];
        if (cleaned.Count > MaxItems)
            return Result.Failure<ValidatedInput>(
                $"A pick list cannot hold more than {MaxItems} entries.",
                "VALIDATION_FAILED"
            );
        if (cleaned.Any(i => i.Length > MaxItemLength))
            return Result.Failure<ValidatedInput>(
                $"A pick list entry cannot exceed {MaxItemLength} characters.",
                "VALIDATION_FAILED"
            );

        return Result.Success(new ValidatedInput(trimmedName, trimmedDescription, cleaned));
    }

    private readonly record struct ValidatedInput(
        string Name,
        string? Description,
        List<string> Items
    );

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex NamePattern();
}
