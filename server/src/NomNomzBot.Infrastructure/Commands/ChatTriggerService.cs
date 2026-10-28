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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Identity.Enums;
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

        // An explicit target wins over the legacy pipeline field.
        BindingTarget? target = request.Target ?? PipelineTargetOf(request.PipelineId);
        Result targetOk = await BindingTargetBinder.ValidateAsync(
            _db,
            broadcaster,
            target,
            cancellationToken
        );
        if (targetOk.IsFailure)
            return targetOk.ToTyped<ChatTriggerDto>();

        Result invalid = Validate(
            request.Pattern,
            request.MatchType,
            request.Response,
            Binds(target)
        );
        if (invalid.IsFailure)
            return Result.Failure<ChatTriggerDto>(invalid.ErrorMessage!, invalid.ErrorCode);

        Result deliveryOk = request.ReplyDelivery.Check();
        if (deliveryOk.IsFailure)
            return deliveryOk.ToTyped<ChatTriggerDto>();

        // Refused, never clamped: Math.Max(0, x) used to accept any integer, so a caller sending a rung
        // that does not exist got the nearest legal-looking value instead of an error.
        int? minLevel = PermissionLevelNames.ToLevelValue(request.MinPermissionLevel);
        if (minLevel is null)
            return Errors
                .ValidationFailed(UnknownRungMessage(request.MinPermissionLevel))
                .ToTyped<ChatTriggerDto>();

        ChatTrigger trigger = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Pattern = request.Pattern.Trim(),
            MatchType = request.MatchType,
            CaseSensitive = request.CaseSensitive,
            IsEnabled = request.IsEnabled,
            Response = request.Response,
            ReplyDelivery = ReplyDelivery.ForStorage(request.ReplyDelivery),
            CooldownSeconds = Math.Clamp(request.CooldownSeconds, 0, MaxCooldownSeconds),
            MinPermissionLevel = minLevel.Value,
        };
        BindingTargetBinder.ApplyTo(trigger, target, null);
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
        // Absent (null) leaves the binding unchanged; a pipeline target with no id (Guid.Empty on the legacy
        // field) clears it (switching back to a text response); anything else binds. A null cannot mean "clear" —
        // the client's explicitNulls=false serializer drops it — so the empty sentinel carries the unbind
        // (matching RewardService's convention). An explicit target wins over the legacy pipeline field.
        BindingTarget? target = request.Target ?? PipelineTargetOf(request.PipelineId);
        Result targetOk = await BindingTargetBinder.ValidateAsync(
            _db,
            broadcaster,
            target,
            cancellationToken
        );
        if (targetOk.IsFailure)
            return targetOk.ToTyped<ChatTriggerDto>();

        bool bound = target is null ? trigger.GetBindingTarget() is not null : Binds(target);
        Result invalid = Validate(pattern, matchType, response, bound);
        if (invalid.IsFailure)
            return Result.Failure<ChatTriggerDto>(invalid.ErrorMessage!, invalid.ErrorCode);

        Result deliveryOk = request.ReplyDelivery.Check();
        if (deliveryOk.IsFailure)
            return deliveryOk.ToTyped<ChatTriggerDto>();

        trigger.Pattern = pattern;
        trigger.MatchType = matchType;
        trigger.Response = response;
        if (request.ReplyDelivery is not null)
            trigger.ReplyDelivery = ReplyDelivery.ForStorage(request.ReplyDelivery);
        BindingTargetBinder.ApplyTo(trigger, target, trigger.PipelineId);
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
        if (request.MinPermissionLevel is not null)
        {
            int? updatedLevel = PermissionLevelNames.ToLevelValue(request.MinPermissionLevel);
            if (updatedLevel is null)
                return Errors
                    .ValidationFailed(UnknownRungMessage(request.MinPermissionLevel))
                    .ToTyped<ChatTriggerDto>();
            trigger.MinPermissionLevel = updatedLevel.Value;
        }

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
    /// The legacy pipeline field as a target: null when absent, a pipeline target with no id when it is the
    /// empty sentinel (which clears the binding).
    /// </summary>
    private static BindingTarget? PipelineTargetOf(Guid? pipelineId) =>
        pipelineId is null
            ? null
            : new(
                BindingTargetKind.Pipeline,
                pipelineId == Guid.Empty ? null : pipelineId.Value.ToString()
            );

    /// <summary>True when the target leaves something bound (a pipeline target without an id clears it).</summary>
    private static bool Binds(BindingTarget? target) =>
        target is not null
        && (
            target.Kind != BindingTargetKind.Pipeline
            || (Guid.TryParse(target.Id, out Guid pipelineId) && pipelineId != Guid.Empty)
        );

    /// <summary>
    /// Write-time honesty checks: a trigger must be able to DO something (template or a bound target), and a
    /// regex pattern must compile — an invalid one would silently never fire.
    /// </summary>
    private static Result Validate(string pattern, string matchType, string? response, bool bound)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return Result.Failure("The pattern cannot be empty.", "VALIDATION_FAILED");
        if (string.IsNullOrWhiteSpace(response) && !bound)
            return Result.Failure(
                "A trigger needs a response template or a bound pipeline, command or action.",
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
            t.GetBindingTarget(),
            t.CooldownSeconds,
            PermissionLevelNames.ToName(t.MinPermissionLevel),
            t.CreatedAt,
            t.UpdatedAt
        )
        {
            ReplyDelivery = t.ReplyDelivery,
        };

    /// <summary>One wording for an unusable rung, naming what IS accepted so the caller can fix it.</summary>
    private static string UnknownRungMessage(string? given) =>
        $"'{given}' is not a permission rung. Use one of: {string.Join(", ", PermissionLevelNames.All)}.";
}
