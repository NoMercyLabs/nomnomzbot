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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// Store catalog + redemptions (economy.md §3.4). Items are CRUD; a purchase enforces the
/// permission/cooldown/stock guards then debits through the ledger and records an immutable purchase; a refund
/// posts a reversing credit and an append-only refunded row. (Deferred — documented: the per-viewer-per-stream
/// cap needs stream context, purchase idempotency needs the IdempotencyKeys store, the PipelineId-exists check
/// is left to the caller, and the reversing entry's RelatedEntryId link awaits a ledger-command field. The
/// purchase debit is atomic on its own; the purchase row is written immediately after.)
/// </summary>
public sealed class CatalogService(
    IApplicationDbContext db,
    ICurrencyAccountService accounts,
    IEventBus eventBus,
    TimeProvider clock
) : ICatalogService
{
    public async Task<Result<PagedList<CatalogItemDto>>> ListItemsAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<CatalogItem> query = db.CatalogItems.Where(i =>
            i.BroadcasterId == broadcasterId && i.DeletedAt == null
        );
        int total = await query.CountAsync(ct);
        List<CatalogItem> rows = await query
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<CatalogItemDto>(
                [.. rows.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<CatalogItemDto>> GetItemAsync(
        Guid broadcasterId,
        Guid itemId,
        CancellationToken ct = default
    )
    {
        CatalogItem? item = await FindItemAsync(broadcasterId, itemId, ct);
        return item is null
            ? Result.Failure<CatalogItemDto>("Item not found.", "NOT_FOUND")
            : Result.Success(ToDto(item));
    }

    public async Task<Result<CatalogItemDto>> CreateItemAsync(
        Guid broadcasterId,
        CreateCatalogItemRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<CatalogItemDto>("Name is required.", "VALIDATION_FAILED");
        if (string.IsNullOrWhiteSpace(request.SinkType))
            return Result.Failure<CatalogItemDto>("Sink type is required.", "VALIDATION_FAILED");
        if (request.Cost < 0)
            return Result.Failure<CatalogItemDto>("Cost cannot be negative.", "VALIDATION_FAILED");

        string normalized = request.Name.Trim().ToLowerInvariant();
        if (
            await db.CatalogItems.AnyAsync(
                i =>
                    i.BroadcasterId == broadcasterId
                    && i.NameNormalized == normalized
                    && i.DeletedAt == null,
                ct
            )
        )
            return Result.Failure<CatalogItemDto>(
                "An item with that name already exists.",
                "ALREADY_EXISTS"
            );

        CatalogItem item = new()
        {
            BroadcasterId = broadcasterId,
            Name = request.Name,
            NameNormalized = normalized,
            Description = request.Description,
            SinkType = request.SinkType,
            Cost = request.Cost,
            IconUrl = request.IconUrl,
            IsEnabled = request.IsEnabled,
            Permission = string.IsNullOrWhiteSpace(request.Permission)
                ? nameof(CommunityStanding.Everyone)
                : request.Permission,
            PipelineId = request.PipelineId,
            CooldownSeconds = request.CooldownSeconds,
            CooldownPerUser = request.CooldownPerUser,
            StockLimit = request.StockLimit,
            StockRemaining = request.StockLimit,
            MaxPerViewerPerStream = request.MaxPerViewerPerStream,
            SortOrder = request.SortOrder,
        };
        db.CatalogItems.Add(item);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(item));
    }

    public async Task<Result<CatalogItemDto>> UpdateItemAsync(
        Guid broadcasterId,
        Guid itemId,
        UpdateCatalogItemRequest request,
        CancellationToken ct = default
    )
    {
        CatalogItem? item = await FindItemAsync(broadcasterId, itemId, ct);
        if (item is null)
            return Result.Failure<CatalogItemDto>("Item not found.", "NOT_FOUND");
        if (request.Cost is < 0)
            return Result.Failure<CatalogItemDto>("Cost cannot be negative.", "VALIDATION_FAILED");

        if (request.Name is not null)
        {
            string normalized = request.Name.Trim().ToLowerInvariant();
            if (
                normalized != item.NameNormalized
                && await db.CatalogItems.AnyAsync(
                    i =>
                        i.BroadcasterId == broadcasterId
                        && i.NameNormalized == normalized
                        && i.Id != itemId
                        && i.DeletedAt == null,
                    ct
                )
            )
                return Result.Failure<CatalogItemDto>(
                    "An item with that name already exists.",
                    "ALREADY_EXISTS"
                );
            item.Name = request.Name;
            item.NameNormalized = normalized;
        }
        if (request.Description is not null)
            item.Description = request.Description;
        if (request.Cost is long cost)
            item.Cost = cost;
        if (request.IconUrl is not null)
            item.IconUrl = request.IconUrl;
        if (request.IsEnabled is bool enabled)
            item.IsEnabled = enabled;
        if (request.Permission is not null)
            item.Permission = request.Permission;
        if (request.PipelineId is not null)
            item.PipelineId = request.PipelineId;
        if (request.CooldownSeconds is int cooldown)
            item.CooldownSeconds = cooldown;
        if (request.CooldownPerUser is bool perUser)
            item.CooldownPerUser = perUser;
        if (request.StockLimit is int stock)
            item.StockLimit = stock;
        if (request.MaxPerViewerPerStream is int maxPer)
            item.MaxPerViewerPerStream = maxPer;
        if (request.SortOrder is int sort)
            item.SortOrder = sort;

        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(item));
    }

    public async Task<Result> DeleteItemAsync(
        Guid broadcasterId,
        Guid itemId,
        CancellationToken ct = default
    )
    {
        CatalogItem? item = await FindItemAsync(broadcasterId, itemId, ct);
        if (item is null)
            return Result.Failure("Item not found.", "NOT_FOUND");
        item.DeletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<CatalogPurchaseDto>> PurchaseAsync(
        Guid broadcasterId,
        PurchaseRequest request,
        CancellationToken ct = default
    )
    {
        CatalogItem? item = await FindItemAsync(broadcasterId, request.ItemId, ct);
        if (item is null || !item.IsEnabled)
            return Result.Failure<CatalogPurchaseDto>("Item not available.", "NOT_FOUND");

        int requiredLevel = Enum.TryParse(
            item.Permission,
            ignoreCase: true,
            out CommunityStanding standing
        )
            ? standing.ToLevel()
            : 0;
        if (request.RoleLevel < requiredLevel)
            return Result.Failure<CatalogPurchaseDto>(
                "Insufficient role to purchase this item.",
                "FORBIDDEN"
            );

        if (item.CooldownSeconds > 0)
        {
            DateTime cutoff = clock.GetUtcNow().UtcDateTime.AddSeconds(-item.CooldownSeconds);
            IQueryable<CatalogPurchase> recent = db.CatalogPurchases.Where(p =>
                p.BroadcasterId == broadcasterId
                && p.CatalogItemId == item.Id
                && p.Status == CatalogPurchaseStatus.Completed
                && p.CreatedAt > cutoff
            );
            if (item.CooldownPerUser)
                recent = recent.Where(p => p.BuyerUserId == request.BuyerUserId);
            if (await recent.AnyAsync(ct))
                return Result.Failure<CatalogPurchaseDto>("Item is on cooldown.", "ON_COOLDOWN");
        }

        if (item.StockLimit is not null && item.StockRemaining <= 0)
            return Result.Failure<CatalogPurchaseDto>("Item is out of stock.", "OUT_OF_STOCK");

        if (item.MaxPerViewerPerStream is int maxPerStream)
        {
            DateTime? streamStart = await CurrentStreamStartAsync(broadcasterId, ct);
            if (streamStart is DateTime since)
            {
                int already = await db.CatalogPurchases.CountAsync(
                    p =>
                        p.BroadcasterId == broadcasterId
                        && p.CatalogItemId == item.Id
                        && p.BuyerUserId == request.BuyerUserId
                        && p.Status == CatalogPurchaseStatus.Completed
                        && p.CreatedAt >= since,
                    ct
                );
                if (already >= maxPerStream)
                    return Result.Failure<CatalogPurchaseDto>(
                        "Per-stream purchase limit reached for this item.",
                        "PER_STREAM_LIMIT"
                    );
            }
        }

        Result<CurrencyLedgerEntryDto> debit = await accounts.PostLedgerEntryAsync(
            broadcasterId,
            new PostLedgerEntryCommand(
                request.BuyerUserId,
                -item.Cost,
                nameof(CurrencyEntryType.SpendCatalog),
                nameof(CurrencyLedgerSourceType.CatalogItem),
                item.Id,
                EventId: null,
                Reason: null,
                ActorUserId: null,
                IdempotencyKey: null
            ),
            ct
        );
        if (debit.IsFailure)
            return Result.Failure<CatalogPurchaseDto>(debit.ErrorMessage, debit.ErrorCode);

        if (item.StockRemaining is int remaining)
            item.StockRemaining = remaining - 1;

        CatalogPurchase purchase = new()
        {
            BroadcasterId = broadcasterId,
            CatalogItemId = item.Id,
            BuyerAccountId = debit.Value.AccountId,
            BuyerUserId = request.BuyerUserId,
            CostPaid = item.Cost,
            ItemNameSnapshot = item.Name,
            Status = CatalogPurchaseStatus.Completed,
            LedgerEntryId = debit.Value.Id,
            InputArgs = request.InputArgs,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.CatalogPurchases.Add(purchase);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new CatalogItemPurchasedEvent
            {
                BroadcasterId = broadcasterId,
                PurchaseId = purchase.Id,
                CatalogItemId = item.Id,
                BuyerUserId = request.BuyerUserId,
                BuyerAccountId = debit.Value.AccountId,
                CostPaid = item.Cost,
                SinkType = item.SinkType,
                PipelineId = item.PipelineId,
                Status = purchase.Status.ToString(),
            },
            ct
        );
        return Result.Success(ToDto(purchase));
    }

    public async Task<Result<CatalogPurchaseDto>> RefundPurchaseAsync(
        Guid broadcasterId,
        long purchaseId,
        RefundRequest request,
        CancellationToken ct = default
    )
    {
        CatalogPurchase? original = await db.CatalogPurchases.FirstOrDefaultAsync(
            p =>
                p.BroadcasterId == broadcasterId
                && p.Id == purchaseId
                && p.Status == CatalogPurchaseStatus.Completed,
            ct
        );
        if (original is null)
            return Result.Failure<CatalogPurchaseDto>("Completed purchase not found.", "NOT_FOUND");

        Result<CurrencyLedgerEntryDto> credit = await accounts.PostLedgerEntryAsync(
            broadcasterId,
            new PostLedgerEntryCommand(
                original.BuyerUserId,
                original.CostPaid,
                nameof(CurrencyEntryType.RefundCatalog),
                nameof(CurrencyLedgerSourceType.CatalogItem),
                original.CatalogItemId,
                EventId: null,
                request.Reason,
                request.ActorUserId,
                IdempotencyKey: null
            ),
            ct
        );
        if (credit.IsFailure)
            return Result.Failure<CatalogPurchaseDto>(credit.ErrorMessage, credit.ErrorCode);

        CatalogItem? item = await db.CatalogItems.FirstOrDefaultAsync(
            i => i.Id == original.CatalogItemId,
            ct
        );
        if (item?.StockRemaining is int remaining)
            item.StockRemaining = remaining + 1;

        CatalogPurchase refundRow = new()
        {
            BroadcasterId = broadcasterId,
            CatalogItemId = original.CatalogItemId,
            BuyerAccountId = original.BuyerAccountId,
            BuyerUserId = original.BuyerUserId,
            CostPaid = original.CostPaid,
            ItemNameSnapshot = original.ItemNameSnapshot,
            Status = CatalogPurchaseStatus.Refunded,
            LedgerEntryId = credit.Value.Id,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.CatalogPurchases.Add(refundRow);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new CatalogPurchaseRefundedEvent
            {
                BroadcasterId = broadcasterId,
                PurchaseId = original.Id,
                CatalogItemId = original.CatalogItemId,
                BuyerUserId = original.BuyerUserId,
                AmountRefunded = original.CostPaid,
                ReversalLedgerEntryId = credit.Value.Id,
            },
            ct
        );
        return Result.Success(ToDto(refundRow));
    }

    public async Task<Result<PagedList<CatalogPurchaseDto>>> ListPurchasesAsync(
        Guid broadcasterId,
        PurchaseFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<CatalogPurchase> query = db.CatalogPurchases.Where(p =>
            p.BroadcasterId == broadcasterId
        );
        if (filter.CatalogItemId is Guid itemId)
            query = query.Where(p => p.CatalogItemId == itemId);
        if (filter.BuyerUserId is Guid buyer)
            query = query.Where(p => p.BuyerUserId == buyer);
        if (
            filter.Status is not null
            && Enum.TryParse(filter.Status, ignoreCase: true, out CatalogPurchaseStatus status)
        )
            query = query.Where(p => p.Status == status);

        int total = await query.CountAsync(ct);
        List<CatalogPurchase> rows = await query
            .OrderByDescending(p => p.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<CatalogPurchaseDto>(
                [.. rows.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    private Task<CatalogItem?> FindItemAsync(
        Guid broadcasterId,
        Guid itemId,
        CancellationToken ct
    ) =>
        db.CatalogItems.FirstOrDefaultAsync(
            i => i.BroadcasterId == broadcasterId && i.Id == itemId && i.DeletedAt == null,
            ct
        );

    /// <summary>The current stream's start (latest stream for the channel), or null when none — then the per-stream cap is moot.</summary>
    private async Task<DateTime?> CurrentStreamStartAsync(Guid broadcasterId, CancellationToken ct)
    {
        DateTimeOffset? startedAt = await db
            .Streams.Where(s => s.ChannelId == broadcasterId)
            .OrderByDescending(s => s.CreatedAt) // latest-created stream = the current one
            .Select(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);
        return startedAt?.UtcDateTime;
    }

    private static CatalogItemDto ToDto(CatalogItem i) =>
        new(
            i.Id,
            i.Name,
            i.Description,
            i.SinkType,
            i.Cost,
            i.IconUrl,
            i.IsEnabled,
            i.Permission,
            i.PipelineId,
            i.CooldownSeconds,
            i.CooldownPerUser,
            i.StockLimit,
            i.StockRemaining,
            i.MaxPerViewerPerStream,
            i.SortOrder,
            i.CreatedAt,
            i.UpdatedAt
        );

    private static CatalogPurchaseDto ToDto(CatalogPurchase p) =>
        new(
            p.Id,
            p.CatalogItemId,
            p.BuyerUserId,
            p.BuyerAccountId,
            p.CostPaid,
            p.ItemNameSnapshot,
            p.Status.ToString(),
            p.LedgerEntryId,
            p.InputArgs,
            p.CreatedAt
        );
}
