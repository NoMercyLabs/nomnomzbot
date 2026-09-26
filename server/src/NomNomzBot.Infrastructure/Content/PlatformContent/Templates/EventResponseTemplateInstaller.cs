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
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.PlatformContent.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// Installs an <c>event_response</c> template: sets the channel's response for the template's event through
/// <see cref="IEventResponseService.UpsertAsync"/> (the same save path as the Event Responses page, so the
/// template-helper and pipeline-ownership checks run), then stamps provenance on the row.
/// </summary>
public sealed class EventResponseTemplateInstaller(
    IApplicationDbContext db,
    IEventResponseService eventResponses,
    ITemplateHelperValidator templateHelperValidator
) : IPlatformTemplateInstaller
{
    private const int MaxMessageLength = 2000;

    public string Kind => PlatformContentKinds.EventResponse;

    public string WriteActionKey => "eventresponses:write";

    public Result ValidatePayload(string payloadJson)
    {
        Result<EventResponseTemplatePayload> parsed =
            PlatformTemplateJson.Parse<EventResponseTemplatePayload>(payloadJson);
        return parsed.IsFailure ? parsed : Validate(parsed.Value);
    }

    public async Task<Result<InstalledPlatformTemplateDto>> InstallAsync(
        PlatformTemplateInstall install,
        CancellationToken ct = default
    )
    {
        Result<EventResponseTemplatePayload> parsed =
            PlatformTemplateJson.Parse<EventResponseTemplatePayload>(install.PayloadJson);
        if (parsed.IsFailure)
            return parsed.WithValue<InstalledPlatformTemplateDto>(null!);
        EventResponseTemplatePayload payload = parsed.Value;

        Result valid = Validate(payload);
        if (valid.IsFailure)
            return valid.WithValue<InstalledPlatformTemplateDto>(null!);

        bool runsPipeline = payload.ResponseType == EventResponseTemplatePayload.Pipeline;
        if (runsPipeline && (install.PipelineId is null || install.PipelineId == Guid.Empty))
            return Result.Failure<InstalledPlatformTemplateDto>(
                "This template runs a pipeline. Choose one of this channel's pipelines.",
                "VALIDATION_FAILED"
            );

        Result<EventResponseDto> upserted = await eventResponses.UpsertAsync(
            install.BroadcasterId.ToString(),
            payload.EventType,
            new()
            {
                IsEnabled = payload.IsEnabled,
                ResponseType = payload.ResponseType,
                Message = payload.Message ?? string.Empty,
                PipelineId = runsPipeline ? install.PipelineId : Guid.Empty,
                Metadata = new Dictionary<string, string>(payload.Metadata),
            },
            ct
        );
        if (upserted.IsFailure)
            return upserted.WithValue<InstalledPlatformTemplateDto>(null!);

        EventResponse row = await db.EventResponses.FirstAsync(
            e => e.Id == upserted.Value.Id && e.BroadcasterId == install.BroadcasterId,
            ct
        );
        if (payload.Message is null)
            row.Message = null;
        row.Stamp(
            install.Source,
            EventResponseTemplatePayload.FromEntity(row).ComputeHash(),
            DateTime.UtcNow
        );
        await db.SaveChangesAsync(ct);

        return Result.Success(new InstalledPlatformTemplateDto(Kind, row.Id, row.EventType));
    }

    private Result Validate(EventResponseTemplatePayload payload)
    {
        if (!EventResponsePresetCatalog.EventTypes.Contains(payload.EventType))
            return Result.Failure(
                $"Unknown event type '{payload.EventType}'.",
                "VALIDATION_FAILED"
            );

        if (!EventResponseTemplatePayload.ResponseTypes.Contains(payload.ResponseType))
            return Result.Failure(
                $"Unknown response type '{payload.ResponseType}'.",
                "VALIDATION_FAILED"
            );

        if (
            payload.ResponseType == EventResponseTemplatePayload.ChatMessage
            && string.IsNullOrWhiteSpace(payload.Message)
        )
            return Result.Failure("A chat message response needs a message.", "VALIDATION_FAILED");

        if (payload.Message is { Length: > MaxMessageLength })
            return Result.Failure(
                $"The message is longer than {MaxMessageLength} characters.",
                "VALIDATION_FAILED"
            );

        return templateHelperValidator.Validate(
            payload.Message,
            TemplateHelperContext.EventResponse
        );
    }
}
