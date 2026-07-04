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
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Quotes.Entities;
using NomNomzBot.Domain.Quotes.Events;

namespace NomNomzBot.Infrastructure.Quotes;

/// <summary>
/// The quote library service (quotes.md §3). Adds run under a transaction so the per-channel number
/// allocation (<see cref="ITenantSequenceAllocator"/>) commits atomically with the row insert; reads/edits
/// stay scoped to the tenant and respect the soft-delete filter.
/// </summary>
public sealed class QuoteService : IQuoteService
{
    /// <summary>The per-tenant sequence this subsystem owns; one stream of numbers per channel (D1).</summary>
    private const string QuoteNumberSequence = "quote_number";

    private readonly IApplicationDbContext _db;
    private readonly ITenantSequenceAllocator _sequences;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public QuoteService(
        IApplicationDbContext db,
        ITenantSequenceAllocator sequences,
        IUnitOfWork unitOfWork,
        IEventBus eventBus,
        TimeProvider timeProvider
    )
    {
        _db = db;
        _sequences = sequences;
        _unitOfWork = unitOfWork;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<Result<QuoteDto>> AddAsync(
        Guid broadcasterId,
        AddQuoteRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return Result.Failure<QuoteDto>("Quote text is required.", "VALIDATION_FAILED");

        string text = request.Text.Trim();
        if (text.Length > 500)
            return Result.Failure<QuoteDto>(
                "Quote text cannot exceed 500 characters.",
                "VALIDATION_FAILED"
            );

        bool channelExists = await _db.Channels.AnyAsync(c => c.Id == broadcasterId, ct);
        if (!channelExists)
            return Errors.ChannelNotFound<QuoteDto>(broadcasterId.ToString());

        // The number allocation and the row insert must land together: allocate inside the transaction so a
        // crash between the two never burns a number or hands out a duplicate (the allocator row-locks the
        // tenant's sequence; the unique (BroadcasterId, Number) index is the final backstop).
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            Result<long> next = await _sequences.NextAsync(broadcasterId, QuoteNumberSequence, ct);
            if (next.IsFailure)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                return Result.Failure<QuoteDto>(next.ErrorMessage, next.ErrorCode);
            }

            DateTime createdAt = _timeProvider.GetUtcNow().UtcDateTime;
            Quote quote = new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                Number = (int)next.Value,
                Text = text,
                QuotedDisplayName = request.QuotedDisplayName?.Trim(),
                ContextGame = request.ContextGame?.Trim(),
                QuotedAt = request.QuotedAt ?? createdAt,
                CreatedByUserId = request.CreatedByUserId,
            };

            _db.Quotes.Add(quote);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);

            await _eventBus.PublishAsync(
                new QuoteAddedEvent
                {
                    BroadcasterId = broadcasterId,
                    QuoteId = quote.Id,
                    Number = quote.Number,
                    CreatedByUserId = quote.CreatedByUserId,
                },
                ct
            );
            await _eventBus.PublishAsync(
                new ChannelConfigChangedEvent
                {
                    BroadcasterId = broadcasterId,
                    Domain = "quotes",
                    EntityId = quote.Id.ToString(),
                    Action = "created",
                },
                ct
            );

            return Result.Success(ToDto(quote));
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<Result<QuoteDto>> GetAsync(
        Guid broadcasterId,
        int number,
        CancellationToken ct = default
    )
    {
        Quote? quote = await _db.Quotes.FirstOrDefaultAsync(
            q => q.BroadcasterId == broadcasterId && q.Number == number,
            ct
        );

        if (quote is null)
            return Errors.NotFound<QuoteDto>("Quote", $"#{number}");

        return Result.Success(ToDto(quote));
    }

    public async Task<Result<QuoteDto>> GetRandomAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        // The soft-delete global filter already excludes deleted rows; count then pick a uniform offset so the
        // selection is uniform over the live set without materialising every quote.
        IQueryable<Quote> query = _db.Quotes.Where(q => q.BroadcasterId == broadcasterId);
        int total = await query.CountAsync(ct);
        if (total == 0)
            return Result.Failure<QuoteDto>("This channel has no quotes yet.", "QUOTES_EMPTY");

        int offset = Random.Shared.Next(total);
        Quote quote = await query.OrderBy(q => q.Number).Skip(offset).Take(1).FirstAsync(ct);

        return Result.Success(ToDto(quote));
    }

    public async Task<Result<PagedList<QuoteDto>>> ListAsync(
        Guid broadcasterId,
        QuoteSearch search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<Quote> query = _db.Quotes.Where(q => q.BroadcasterId == broadcasterId);

        if (!string.IsNullOrWhiteSpace(search.Term))
        {
            string term = search.Term.Trim();
            // Provider-appropriate case-insensitive contains (EF maps to ILIKE on Npgsql, a collation-aware
            // LIKE elsewhere) over the body and the attribution (quotes.md §1).
            query = query.Where(q =>
                EF.Functions.Like(q.Text, $"%{term}%")
                || (
                    q.QuotedDisplayName != null
                    && EF.Functions.Like(q.QuotedDisplayName, $"%{term}%")
                )
            );
        }

        int total = await query.CountAsync(ct);

        List<QuoteDto> items = await query
            .OrderByDescending(q => q.Number)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(q => new QuoteDto(
                q.Id,
                q.Number,
                q.Text,
                q.QuotedDisplayName,
                q.ContextGame,
                q.QuotedAt,
                q.CreatedAt
            ))
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<QuoteDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<QuoteDto>> EditAsync(
        Guid broadcasterId,
        int number,
        EditQuoteRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return Result.Failure<QuoteDto>("Quote text is required.", "VALIDATION_FAILED");

        string text = request.Text.Trim();
        if (text.Length > 500)
            return Result.Failure<QuoteDto>(
                "Quote text cannot exceed 500 characters.",
                "VALIDATION_FAILED"
            );

        Quote? quote = await _db.Quotes.FirstOrDefaultAsync(
            q => q.BroadcasterId == broadcasterId && q.Number == number,
            ct
        );

        if (quote is null)
            return Errors.NotFound<QuoteDto>("Quote", $"#{number}");

        // Number is immutable (quotes.md §3) — only the body and attribution change.
        quote.Text = text;
        quote.QuotedDisplayName = request.QuotedDisplayName?.Trim();
        quote.ContextGame = request.ContextGame?.Trim();

        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "quotes",
                EntityId = quote.Id.ToString(),
                Action = "updated",
            },
            ct
        );

        return Result.Success(ToDto(quote));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        int number,
        CancellationToken ct = default
    )
    {
        Quote? quote = await _db.Quotes.FirstOrDefaultAsync(
            q => q.BroadcasterId == broadcasterId && q.Number == number,
            ct
        );

        if (quote is null)
            return Errors.NotFound<QuoteDto>("Quote", $"#{number}");

        // Soft-delete via the SoftDeleteInterceptor; the row (and its number) is retained so the number is
        // never reused (D1).
        _db.Quotes.Remove(quote);
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "quotes",
                EntityId = quote.Id.ToString(),
                Action = "deleted",
            },
            ct
        );

        return Result.Success();
    }

    private static QuoteDto ToDto(Quote q) =>
        new(q.Id, q.Number, q.Text, q.QuotedDisplayName, q.ContextGame, q.QuotedAt, q.CreatedAt);
}
