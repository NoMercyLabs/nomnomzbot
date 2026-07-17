// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// CRUD for a channel's custom chat filters (moderation.md J.6). Stores <c>Terms</c> as a JSON string column
/// (<see cref="ChatFilter.TermsJson"/>) so the entity stays provider-agnostic; the enforcement handler and this
/// service both read that same column.
/// </summary>
public sealed class ChatFilterService(IApplicationDbContext db) : IChatFilterService
{
    public async Task<Result<PagedList<ChatFilterDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<ChatFilter> query = db
            .ChatFilters.Where(f => f.BroadcasterId == broadcasterId)
            .OrderByDescending(f => f.CreatedAt);

        int total = await query.CountAsync(ct);
        List<ChatFilter> page = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<ChatFilterDto> items = page.Select(ToDto).ToList();
        return Result.Success(
            new PagedList<ChatFilterDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<ChatFilterDto>> CreateAsync(
        Guid broadcasterId,
        CreateChatFilterRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<ChatFilterDto>("A filter needs a name.", "VALIDATION_FAILED");

        Result compileCheck = ValidateRegex(request.FilterType, request.Pattern);
        if (compileCheck.IsFailure)
            return Result.Failure<ChatFilterDto>(
                compileCheck.ErrorMessage!,
                compileCheck.ErrorCode!
            );

        if (request.Action == ChatFilterAction.Timeout && request.TimeoutSeconds is not > 0)
            return Result.Failure<ChatFilterDto>(
                "A timeout filter needs a positive duration.",
                "VALIDATION_FAILED"
            );

        ChatFilter filter = new()
        {
            BroadcasterId = broadcasterId,
            FilterType = request.FilterType,
            Name = request.Name,
            Pattern = request.Pattern,
            TermsJson = SerializeTerms(request.Terms),
            LinkPolicyJson = request.LinkPolicyJson,
            Action = request.Action,
            TimeoutSeconds = request.TimeoutSeconds,
            ExemptMinRoleLevel = request.ExemptMinRoleLevel,
            IsEnabled = request.IsEnabled,
            IsCaseSensitive = request.IsCaseSensitive,
        };

        db.ChatFilters.Add(filter);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(filter));
    }

    public async Task<Result<ChatFilterDto>> UpdateAsync(
        Guid broadcasterId,
        Guid filterId,
        UpdateChatFilterRequest request,
        CancellationToken ct = default
    )
    {
        ChatFilter? filter = await db.ChatFilters.FirstOrDefaultAsync(
            f => f.BroadcasterId == broadcasterId && f.Id == filterId,
            ct
        );
        if (filter is null)
            return Result.Failure<ChatFilterDto>("Filter not found.", "NOT_FOUND");

        if (request.Pattern is not null)
        {
            Result compileCheck = ValidateRegex(filter.FilterType, request.Pattern);
            if (compileCheck.IsFailure)
                return Result.Failure<ChatFilterDto>(
                    compileCheck.ErrorMessage!,
                    compileCheck.ErrorCode!
                );
            filter.Pattern = request.Pattern;
        }

        if (request.Name is not null)
            filter.Name = request.Name;
        if (request.Action is { } action)
            filter.Action = action;
        if (request.Terms is not null)
            filter.TermsJson = SerializeTerms(request.Terms);
        if (request.LinkPolicyJson is not null)
            filter.LinkPolicyJson = request.LinkPolicyJson;
        if (request.TimeoutSeconds is not null)
            filter.TimeoutSeconds = request.TimeoutSeconds;
        if (request.ExemptMinRoleLevel is { } exempt)
            filter.ExemptMinRoleLevel = exempt;
        if (request.IsEnabled is { } enabled)
            filter.IsEnabled = enabled;
        if (request.IsCaseSensitive is { } caseSensitive)
            filter.IsCaseSensitive = caseSensitive;

        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(filter));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid filterId,
        CancellationToken ct = default
    )
    {
        ChatFilter? filter = await db.ChatFilters.FirstOrDefaultAsync(
            f => f.BroadcasterId == broadcasterId && f.Id == filterId,
            ct
        );
        if (filter is null)
            return Result.Failure("Filter not found.", "NOT_FOUND");

        db.ChatFilters.Remove(filter); // soft-deleted by SoftDeleteInterceptor
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Result ValidateRegex(ChatFilterType type, string? pattern)
    {
        if (type != ChatFilterType.Regex)
            return Result.Success();
        if (string.IsNullOrEmpty(pattern))
            return Result.Failure("A regex filter needs a pattern.", "VALIDATION_FAILED");
        try
        {
            _ = Regex.Match(string.Empty, pattern);
            return Result.Success();
        }
        catch (ArgumentException)
        {
            return Result.Failure("The regex pattern does not compile.", "VALIDATION_FAILED");
        }
    }

    private static string? SerializeTerms(List<string>? terms) =>
        terms is { Count: > 0 } ? JsonSerializer.Serialize(terms) : null;

    private static IReadOnlyList<string> DeserializeTerms(string? termsJson)
    {
        if (string.IsNullOrEmpty(termsJson))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(termsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ChatFilterDto ToDto(ChatFilter f) =>
        new(
            f.Id,
            f.FilterType,
            f.Name,
            f.Pattern,
            DeserializeTerms(f.TermsJson),
            f.Action,
            f.TimeoutSeconds,
            f.ExemptMinRoleLevel,
            f.IsEnabled,
            f.IsCaseSensitive,
            f.MatchCount,
            f.CreatedAt,
            f.UpdatedAt
        );
}
