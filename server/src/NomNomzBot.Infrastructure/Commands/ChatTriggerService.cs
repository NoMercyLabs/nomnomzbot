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
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands;

/// <inheritdoc cref="IChatTriggerService"/>
public sealed class ChatTriggerService : IChatTriggerService
{
    private const int MaxCooldownSeconds = 86_400;

    private readonly IApplicationDbContext _db;
    private readonly IChannelRegistry _registry;

    public ChatTriggerService(IApplicationDbContext db, IChannelRegistry registry)
    {
        _db = db;
        _registry = registry;
    }

    public async Task<Result<IReadOnlyList<ChatTriggerDto>>> ListAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<IReadOnlyList<ChatTriggerDto>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        List<ChatTrigger> triggers = await _db
            .ChatTriggers.Where(t => t.BroadcasterId == broadcaster)
            .OrderBy(t => t.Pattern)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ChatTriggerDto>>([.. triggers.Select(ToDto)]);
    }

    public async Task<Result<ChatTriggerDto>> CreateAsync(
        string broadcasterId,
        CreateChatTriggerRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<ChatTriggerDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        Result invalid = Validate(
            request.Pattern,
            request.MatchType,
            request.Response,
            request.PipelineId
        );
        if (invalid.IsFailure)
            return Result.Failure<ChatTriggerDto>(invalid.ErrorMessage!, invalid.ErrorCode);

        ChatTrigger trigger = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Pattern = request.Pattern.Trim(),
            MatchType = request.MatchType,
            CaseSensitive = request.CaseSensitive,
            IsEnabled = request.IsEnabled,
            Response = request.Response,
            PipelineId = request.PipelineId,
            CooldownSeconds = Math.Clamp(request.CooldownSeconds, 0, MaxCooldownSeconds),
            MinPermissionLevel = Math.Max(0, request.MinPermissionLevel),
        };
        _db.ChatTriggers.Add(trigger);
        await _db.SaveChangesAsync(cancellationToken);
        await _registry.InvalidateChatTriggersAsync(broadcaster, cancellationToken);

        return Result.Success(ToDto(trigger));
    }

    public async Task<Result<ChatTriggerDto>> UpdateAsync(
        string broadcasterId,
        Guid triggerId,
        UpdateChatTriggerRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<ChatTriggerDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        ChatTrigger? trigger = await _db.ChatTriggers.FirstOrDefaultAsync(
            t => t.Id == triggerId && t.BroadcasterId == broadcaster,
            cancellationToken
        );
        if (trigger is null)
            return Errors.NotFound<ChatTriggerDto>("ChatTrigger", triggerId.ToString());

        string pattern = request.Pattern?.Trim() ?? trigger.Pattern;
        string matchType = request.MatchType ?? trigger.MatchType;
        string? response = request.Response ?? trigger.Response;
        Guid? pipelineId = request.PipelineId ?? trigger.PipelineId;
        Result invalid = Validate(pattern, matchType, response, pipelineId);
        if (invalid.IsFailure)
            return Result.Failure<ChatTriggerDto>(invalid.ErrorMessage!, invalid.ErrorCode);

        trigger.Pattern = pattern;
        trigger.MatchType = matchType;
        trigger.Response = response;
        trigger.PipelineId = pipelineId;
        if (request.CaseSensitive.HasValue)
            trigger.CaseSensitive = request.CaseSensitive.Value;
        if (request.IsEnabled.HasValue)
            trigger.IsEnabled = request.IsEnabled.Value;
        if (request.CooldownSeconds.HasValue)
            trigger.CooldownSeconds = Math.Clamp(
                request.CooldownSeconds.Value,
                0,
                MaxCooldownSeconds
            );
        if (request.MinPermissionLevel.HasValue)
            trigger.MinPermissionLevel = Math.Max(0, request.MinPermissionLevel.Value);

        await _db.SaveChangesAsync(cancellationToken);
        await _registry.InvalidateChatTriggersAsync(broadcaster, cancellationToken);

        return Result.Success(ToDto(trigger));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        Guid triggerId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        ChatTrigger? trigger = await _db.ChatTriggers.FirstOrDefaultAsync(
            t => t.Id == triggerId && t.BroadcasterId == broadcaster,
            cancellationToken
        );
        if (trigger is null)
            return Result.Failure($"ChatTrigger '{triggerId}' was not found.", "NOT_FOUND");

        _db.ChatTriggers.Remove(trigger);
        await _db.SaveChangesAsync(cancellationToken);
        await _registry.InvalidateChatTriggersAsync(broadcaster, cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Write-time honesty checks: a trigger must be able to DO something (template or pipeline), and a
    /// regex pattern must compile — an invalid one would silently never fire.
    /// </summary>
    private static Result Validate(
        string pattern,
        string matchType,
        string? response,
        Guid? pipelineId
    )
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return Result.Failure("The pattern cannot be empty.", "VALIDATION_FAILED");
        if (string.IsNullOrWhiteSpace(response) && pipelineId is null)
            return Result.Failure(
                "A trigger needs a response template or a bound pipeline.",
                "VALIDATION_FAILED"
            );
        if (matchType == ChatTriggerMatchType.Regex)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(
                    pattern,
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromMilliseconds(100)
                );
            }
            catch (ArgumentException)
            {
                return Result.Failure("The regex pattern does not compile.", "VALIDATION_FAILED");
            }
        }
        return Result.Success();
    }

    private static ChatTriggerDto ToDto(ChatTrigger t) =>
        new(
            t.Id,
            t.Pattern,
            t.MatchType,
            t.CaseSensitive,
            t.IsEnabled,
            t.Response,
            t.PipelineId,
            t.CooldownSeconds,
            t.MinPermissionLevel,
            t.CreatedAt,
            t.UpdatedAt
        );
}
