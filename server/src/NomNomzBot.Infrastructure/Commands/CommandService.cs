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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands;

public class CommandService : ICommandService
{
    // Mirrors ModerationService's own moderation_action row, but for a destructive command-authoring event
    // rather than a Twitch-facing viewer action — kept as its own record type so the moderation action log's
    // Twitch-target assumptions (TargetUserId resolved as a Twitch id) are never mixed with command names.
    private const string AuditRecordType = "command_action";

    private readonly IApplicationDbContext _db;
    private readonly IPipelineEngine _pipelineEngine;
    private readonly IChannelRegistry _registry;
    private readonly IEventBus _eventBus;
    private readonly IResourceQuotaService _quota;
    private readonly ITemplateHelperValidator _templateHelperValidator;

    public CommandService(
        IApplicationDbContext db,
        IPipelineEngine pipelineEngine,
        IChannelRegistry registry,
        IEventBus eventBus,
        IResourceQuotaService quota,
        ITemplateHelperValidator templateHelperValidator
    )
    {
        _db = db;
        _pipelineEngine = pipelineEngine;
        _registry = registry;
        _eventBus = eventBus;
        _quota = quota;
        _templateHelperValidator = templateHelperValidator;
    }

    public async Task<Result<CommandDto>> CreateAsync(
        string broadcasterId,
        CreateCommandDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<CommandDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        Result<string> normalizedName = await NormalizeAndValidateNameAsync(
            broadcaster,
            request.Name,
            request.PrefixMode,
            request.CustomPrefix,
            cancellationToken
        );
        if (normalizedName.IsFailure)
            return normalizedName.ToTyped<CommandDto>();

        string name = normalizedName.Value;
        string nameNormalized = name.ToLowerInvariant();

        Result templateOk = ValidateTemplateResponses(
            request.Tier,
            request.TemplateResponse,
            request.TemplateResponses
        );
        if (templateOk.IsFailure)
            return templateOk.ToTyped<CommandDto>();

        Result helperOk = ValidateTemplateHelpers(
            request.TemplateResponse,
            request.TemplateResponses
        );
        if (helperOk.IsFailure)
            return helperOk.ToTyped<CommandDto>();

        bool exists = await _db.Commands.AnyAsync(
            c => c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized,
            cancellationToken
        );

        if (exists)
            return Errors.AlreadyExists("command", name).ToTyped<CommandDto>();

        // custom_commands is NEAR_FREE (S-BUDGETS-a): one DB row, checked against the registry's uniform
        // safety baseline via the quota seam — never tier-scaled, self-host included.
        int existingCommandCount = await _db.Commands.CountAsync(
            c => c.BroadcasterId == broadcaster,
            cancellationToken
        );
        Result<QuotaCheckDto> commandQuota = await _quota.CheckAsync(
            broadcaster,
            "custom_commands",
            existingCommandCount + 1,
            cancellationToken
        );
        if (commandQuota.IsFailure)
            return commandQuota.ToTyped<CommandDto>();
        if (!commandQuota.Value.Allowed)
            return Errors
                .QuotaExceeded("custom commands", commandQuota.Value.Limit)
                .ToTyped<CommandDto>();

        Result variationsOk = await CheckVariationCapAsync(
            broadcaster,
            request.TemplateResponses?.Count ?? 0,
            cancellationToken
        );
        if (variationsOk.IsFailure)
            return variationsOk.ToTyped<CommandDto>();

        Command command = new()
        {
            BroadcasterId = broadcaster,
            Name = name,
            NameNormalized = nameNormalized,
            Tier = request.Tier,
            MinPermissionLevel = request.MinPermissionLevel,
            PrefixMode = request.PrefixMode,
            CustomPrefix = request.CustomPrefix,
            MatchMode = request.MatchMode,
            MatchPattern = request.MatchPattern,
            TemplateResponse = request.TemplateResponse,
            TemplateResponses = request.TemplateResponses ?? [],
            PipelineId = request.PipelineId,
            CooldownSeconds = request.CooldownSeconds,
            UserCooldownSeconds = request.UserCooldownSeconds,
            CooldownPerUser = request.CooldownPerUser,
            Description = request.Description,
            Aliases = request.Aliases ?? [],
            IsEnabled = request.IsEnabled,
        };

        _db.Commands.Add(command);
        await _db.SaveChangesAsync(cancellationToken);
        await _registry.InvalidateCommandsAsync(broadcaster, cancellationToken);
        await PublishConfigChangedAsync(broadcaster, command.Id, "created", cancellationToken);

        return Result.Success(ToDto(command));
    }

    public async Task<Result<CommandDto>> UpdateAsync(
        string broadcasterId,
        string commandName,
        UpdateCommandDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<CommandDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        string nameNormalized = commandName.ToLowerInvariant();

        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized,
            cancellationToken
        );

        if (command is null)
            return Errors.NotFound<CommandDto>("Command", commandName);

        // Validated against the FULLY MERGED state BEFORE any mutation — the tracked `command` entity must
        // never be left mutated on a rejected update (this DbContext's identity map would keep returning that
        // unsaved, invalid in-memory state to later reads even though nothing was ever persisted). A template
        // command's responses can be emptied by clearing TemplateResponses while leaving TemplateResponse
        // untouched-but-already-null, and that combination must be caught here too, not just on create.
        Result templateOk = ValidateTemplateResponses(
            request.Tier ?? command.Tier,
            request.TemplateResponse ?? command.TemplateResponse,
            request.TemplateResponses ?? command.TemplateResponses
        );
        if (templateOk.IsFailure)
            return templateOk.ToTyped<CommandDto>();

        Result helperOk = ValidateTemplateHelpers(
            request.TemplateResponse ?? command.TemplateResponse,
            request.TemplateResponses ?? command.TemplateResponses
        );
        if (helperOk.IsFailure)
            return helperOk.ToTyped<CommandDto>();

        if (request.Tier is not null)
            command.Tier = request.Tier;
        if (request.MinPermissionLevel.HasValue)
            command.MinPermissionLevel = request.MinPermissionLevel.Value;
        if (request.PrefixMode is not null)
            command.PrefixMode = request.PrefixMode;
        if (request.CustomPrefix is not null)
            command.CustomPrefix = request.CustomPrefix.Length == 0 ? null : request.CustomPrefix;
        if (request.MatchMode is not null)
            command.MatchMode = request.MatchMode;
        if (request.MatchPattern is not null)
            command.MatchPattern = request.MatchPattern.Length == 0 ? null : request.MatchPattern;
        if (request.TemplateResponse is not null)
            command.TemplateResponse = request.TemplateResponse;
        if (request.TemplateResponses is not null)
        {
            Result variationsOk = await CheckVariationCapAsync(
                broadcaster,
                request.TemplateResponses.Count,
                cancellationToken
            );
            if (variationsOk.IsFailure)
                return variationsOk.ToTyped<CommandDto>();
            command.TemplateResponses = request.TemplateResponses;
        }
        if (request.PipelineId.HasValue)
            command.PipelineId = request.PipelineId.Value;
        if (request.CooldownSeconds.HasValue)
            command.CooldownSeconds = request.CooldownSeconds.Value;
        if (request.UserCooldownSeconds.HasValue)
            command.UserCooldownSeconds = request.UserCooldownSeconds.Value;
        if (request.CooldownPerUser.HasValue)
            command.CooldownPerUser = request.CooldownPerUser.Value;
        if (request.Description is not null)
            command.Description = request.Description;
        if (request.Aliases is not null)
            command.Aliases = request.Aliases;
        if (request.IsEnabled.HasValue)
            command.IsEnabled = request.IsEnabled.Value;

        await _db.SaveChangesAsync(cancellationToken);
        await _registry.InvalidateCommandsAsync(broadcaster, cancellationToken);
        await PublishConfigChangedAsync(broadcaster, command.Id, "updated", cancellationToken);

        return Result.Success(ToDto(command));
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        string commandName,
        string? actorId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        string nameNormalized = commandName.ToLowerInvariant();

        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized,
            cancellationToken
        );

        if (command is null)
            return Result.Failure($"Command '{commandName}' was not found.", "NOT_FOUND");

        Guid commandId = command.Id;

        // Record the deletion to the channel's audit trail BEFORE the row is removed — a destructive, no-undo
        // action with nothing else naming who did it once the Command row is gone.
        _db.Records.Add(
            new()
            {
                BroadcasterId = broadcaster,
                RecordType = AuditRecordType,
                Data = System.Text.Json.JsonSerializer.Serialize(
                    new AuditActionData { Action = "command_deleted", Subject = command.Name }
                ),
                UserId = actorId ?? broadcaster.ToString(),
            }
        );

        _db.Commands.Remove(command);
        await _db.SaveChangesAsync(cancellationToken);
        await _registry.InvalidateCommandsAsync(broadcaster, cancellationToken);
        await PublishConfigChangedAsync(broadcaster, commandId, "deleted", cancellationToken);

        return Result.Success();
    }

    public async Task<Result<CommandDto>> GetAsync(
        string broadcasterId,
        string commandName,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<CommandDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        string nameNormalized = commandName.ToLowerInvariant();

        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized,
            cancellationToken
        );

        if (command is null)
            return Errors.NotFound<CommandDto>("Command", commandName);

        return Result.Success(ToDto(command));
    }

    public async Task<Result<PagedList<CommandListItem>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<PagedList<CommandListItem>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        IQueryable<Command> query = _db.Commands.Where(c => c.BroadcasterId == broadcaster);
        int total = await query.CountAsync(cancellationToken);

        List<CommandListItem> items = await query
            .OrderBy(c => c.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(c => new CommandListItem(
                c.Id,
                c.Name,
                c.Tier,
                c.MinPermissionLevel,
                c.IsEnabled,
                c.PrefixMode,
                c.CustomPrefix,
                c.MatchMode,
                c.MatchPattern,
                c.CooldownSeconds,
                c.UserCooldownSeconds,
                c.CooldownPerUser,
                c.Description,
                c.Aliases,
                c.UseCount,
                c.CreatedAt,
                c.TemplateResponse,
                c.TemplateResponses,
                c.PipelineId
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<CommandListItem>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<string>> ExecuteAsync(
        string broadcasterId,
        string commandName,
        string userId,
        string? input = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<string>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        string nameNormalized = commandName.ToLowerInvariant();

        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c =>
                c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized && c.IsEnabled,
            cancellationToken
        );

        if (command is null)
            return Errors.NotFound<string>("Command", commandName);

        if (command is { Tier: "pipeline", PipelineId: not null })
        {
            // Load the pipeline's graph cache to drive the engine (steps-first engine is Slice 4).
            Pipeline? pipeline = await _db.Pipelines.FirstOrDefaultAsync(
                p => p.Id == command.PipelineId.Value,
                cancellationToken
            );

            string graphJson = pipeline?.GraphJsonCache ?? "{}";

            PipelineRequest pipelineRequest = new()
            {
                BroadcasterId = broadcaster,
                PipelineId = command.PipelineId,
                PipelineJson = graphJson,
                TriggeredByUserId = userId,
                TriggeredByDisplayName = userId,
                RawMessage = input ?? string.Empty,
            };

            PipelineExecutionResult execResult = await _pipelineEngine.ExecuteAsync(
                pipelineRequest,
                cancellationToken
            );

            return Result.Success(execResult.Outcome.ToString());
        }

        // Template tier: pick a response.
        string? response =
            command.TemplateResponse
            ?? (command.TemplateResponses is { Count: > 0 } ? command.TemplateResponses[0] : null);

        return Result.Success(response ?? string.Empty);
    }

    /// <summary>
    /// Guards a data-integrity defect confirmed on the live database: a user typed the channel's own command
    /// prefix into the Name field (e.g. <c>"!so"</c>). With <c>PrefixMode=Default</c> the dispatcher builds the
    /// trigger as <c>channelPrefix + Name</c> (<c>ChatMessageHandler.ResolveAuthoredCommand</c>), so a name that
    /// already starts with its own effective prefix can never match — the saved command is permanently dead and,
    /// because unrecognised input is silent by design, the author never finds out why. Strips one leading
    /// occurrence of the command's OWN effective prefix (chosen over rejecting: typing "!so" unambiguously means
    /// the "so" command, so silently saving the sane form is friendlier — the caller sees the corrected name come
    /// back in the response DTO, so the correction is never a surprise) and then validates what remains: empty or
    /// whitespace-containing names are rejected outright, since neither can ever match a single-token chat command.
    /// </summary>
    private async Task<Result<string>> NormalizeAndValidateNameAsync(
        Guid broadcaster,
        string rawName,
        string prefixMode,
        string? customPrefix,
        CancellationToken cancellationToken
    )
    {
        string trimmed = rawName.Trim();
        if (trimmed.Length == 0)
            return Errors.ValidationFailed("Command name cannot be empty.").ToTyped<string>();

        if (trimmed.Any(char.IsWhiteSpace))
            return Errors
                .ValidationFailed(
                    "Command name cannot contain spaces — a name with a space can never match a single-token chat command."
                )
                .ToTyped<string>();

        string channelPrefix = await ResolveChannelPrefixAsync(broadcaster, cancellationToken);
        string effectivePrefix = EffectivePrefix(prefixMode, customPrefix, channelPrefix);

        string stripped =
            effectivePrefix.Length > 0
            && trimmed.StartsWith(effectivePrefix, StringComparison.Ordinal)
                ? trimmed[effectivePrefix.Length..]
                : trimmed;

        return stripped.Length == 0
            ? Errors
                .ValidationFailed(
                    $"Command name is only the command prefix ('{effectivePrefix}') — the prefix is added automatically when the command fires, so the name itself must not include it."
                )
                .ToTyped<string>()
            : Result.Success(stripped);
    }

    /// <summary>Mirrors <c>ChatMessageHandler.ResolveAuthoredCommand</c>'s trigger-prefix resolution exactly, so
    /// the name saved here is guaranteed to match what the dispatcher will build at runtime.</summary>
    private static string EffectivePrefix(
        string prefixMode,
        string? customPrefix,
        string channelPrefix
    ) =>
        prefixMode switch
        {
            "Custom" => customPrefix ?? string.Empty,
            "None" => string.Empty,
            _ => channelPrefix,
        };

    private async Task<string> ResolveChannelPrefixAsync(Guid broadcaster, CancellationToken ct)
    {
        string? prefix = await _db
            .Channels.Where(c => c.Id == broadcaster)
            .Select(c => c.CommandPrefix)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(prefix) ? "!" : prefix;
    }

    /// <summary>
    /// A <c>template</c>-tier command with no response fires and sends nothing — a second silent-failure mode
    /// found alongside the prefix defect (the same live row that motivated this fix also had
    /// <c>TemplateResponses=[]</c>). Rejected outright rather than allowed through, since an empty template
    /// command can never do anything useful and the author gets no other signal that it is broken.
    /// </summary>
    private static Result ValidateTemplateResponses(
        string tier,
        string? templateResponse,
        List<string>? templateResponses
    ) =>
        tier == "template"
        && string.IsNullOrWhiteSpace(templateResponse)
        && templateResponses is not { Count: > 0 }
            ? Errors.ValidationFailed(
                "A template command needs at least one response — set TemplateResponse or add a TemplateResponses entry."
            )
            : Result.Success();

    /// <summary>S042 save-time guard: every response variant is checked against the Command-context
    /// helper registry so an unknown/misspelled placeholder (or one only valid elsewhere, e.g. an
    /// event-response-only key) is rejected before it reaches the database.</summary>
    private Result ValidateTemplateHelpers(
        string? templateResponse,
        List<string>? templateResponses
    )
    {
        Result single = _templateHelperValidator.Validate(
            templateResponse,
            TemplateHelperContext.Command
        );
        if (single.IsFailure)
            return single;

        foreach (string variant in templateResponses ?? [])
        {
            Result variantResult = _templateHelperValidator.Validate(
                variant,
                TemplateHelperContext.Command
            );
            if (variantResult.IsFailure)
                return variantResult;
        }

        return Result.Success();
    }

    /// <summary>
    /// The per-trigger variation cap (<c>response_variations_per_trigger</c>) — NEAR_FREE, the registry's
    /// uniform safety baseline, never tier-scaled.
    /// </summary>
    private async Task<Result> CheckVariationCapAsync(
        Guid broadcaster,
        int requestedCount,
        CancellationToken ct
    )
    {
        Result<QuotaCheckDto> check = await _quota.CheckAsync(
            broadcaster,
            "response_variations_per_trigger",
            requestedCount,
            ct
        );
        if (check.IsFailure)
            return check;
        return check.Value.Allowed
            ? Result.Success()
            : Errors.QuotaExceeded("response variations per command", check.Value.Limit);
    }

    /// <summary>E5 dashboard live-sync: fired after every successful write so other open dashboards refetch.</summary>
    private Task PublishConfigChangedAsync(
        Guid broadcasterId,
        Guid commandId,
        string action,
        CancellationToken cancellationToken
    ) =>
        _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "commands",
                EntityId = commandId.ToString(),
                Action = action,
            },
            cancellationToken
        );

    private static CommandDto ToDto(Command c) =>
        new(
            c.Id,
            c.Name,
            c.Tier,
            c.MinPermissionLevel,
            c.IsEnabled,
            c.PrefixMode,
            c.CustomPrefix,
            c.MatchMode,
            c.MatchPattern,
            c.TemplateResponse,
            c.TemplateResponses,
            c.PipelineId,
            c.CooldownSeconds,
            c.UserCooldownSeconds,
            c.CooldownPerUser,
            c.Description,
            c.Aliases,
            c.UseCount,
            c.CreatedAt,
            c.UpdatedAt
        );

    /// <summary>The recorded shape of a <see cref="AuditRecordType"/> row — one destructive command-authoring event.</summary>
    private sealed class AuditActionData
    {
        public string Action { get; set; } = string.Empty;
        public string? Subject { get; set; }
    }
}
