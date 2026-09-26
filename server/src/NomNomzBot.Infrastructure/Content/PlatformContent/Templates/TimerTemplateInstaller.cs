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
using NomNomzBot.Domain.PlatformContent.Entities;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// Installs a <c>timer</c> template: adds a new timer to the channel through
/// <see cref="ITimerManagementService.CreateAsync"/> (so the timer limit, the message-variation limit, the
/// template-helper check and pipeline ownership all apply), then stamps provenance. A name the channel already
/// uses gets a free numbered name — install never replaces a timer the channel made itself.
/// </summary>
public sealed class TimerTemplateInstaller(
    IApplicationDbContext db,
    ITimerManagementService timers,
    ITemplateHelperValidator templateHelperValidator
) : IPlatformTemplateInstaller
{
    private const int MaxNameLength = 100;
    private const int MaxIntervalMinutes = 1440;
    private const int MaxMinChatActivity = 10000;

    public string Kind => PlatformContentKinds.Timer;

    public string WriteActionKey => "timers:write";

    public Result ValidatePayload(string payloadJson)
    {
        Result<TimerTemplatePayload> parsed = PlatformTemplateJson.Parse<TimerTemplatePayload>(
            payloadJson
        );
        return parsed.IsFailure ? parsed : Validate(parsed.Value);
    }

    public async Task<Result<InstalledPlatformTemplateDto>> InstallAsync(
        PlatformTemplateInstall install,
        CancellationToken ct = default
    )
    {
        Result<TimerTemplatePayload> parsed = PlatformTemplateJson.Parse<TimerTemplatePayload>(
            install.PayloadJson
        );
        if (parsed.IsFailure)
            return parsed.WithValue<InstalledPlatformTemplateDto>(null!);
        TimerTemplatePayload payload = parsed.Value;

        Result valid = Validate(payload);
        if (valid.IsFailure)
            return valid.WithValue<InstalledPlatformTemplateDto>(null!);

        Guid? pipelineId = install.PipelineId == Guid.Empty ? null : install.PipelineId;
        if (payload.RunsPipelineOnly && pipelineId is null)
            return Result.Failure<InstalledPlatformTemplateDto>(
                "This timer has no messages and runs a pipeline. Choose one of this channel's pipelines.",
                "VALIDATION_FAILED"
            );

        string? name = await PlatformTemplateNames.FreeNameAsync(
            payload.Name,
            MaxNameLength,
            candidate =>
                db.Timers.AnyAsync(
                    t => t.BroadcasterId == install.BroadcasterId && t.Name == candidate,
                    ct
                )
        );
        if (name is null)
            return Result.Failure<InstalledPlatformTemplateDto>(
                $"This channel already has too many timers named '{payload.Name}'.",
                "ALREADY_EXISTS"
            );

        Result<TimerDto> created = await timers.CreateAsync(
            install.BroadcasterId.ToString(),
            new()
            {
                Name = name,
                Messages = [.. payload.Messages],
                PipelineId = pipelineId,
                IntervalMinutes = payload.IntervalMinutes,
                MinChatActivity = payload.MinChatActivity,
                IsEnabled = payload.IsEnabled,
                FireOnce = payload.FireOnce,
            },
            ct
        );
        if (created.IsFailure)
            return created.WithValue<InstalledPlatformTemplateDto>(null!);

        DomainTimer row = await db.Timers.FirstAsync(
            t => t.Id == created.Value.Id && t.BroadcasterId == install.BroadcasterId,
            ct
        );
        row.Stamp(
            install.Source,
            TimerTemplatePayload.FromEntity(row).ComputeHash(),
            DateTime.UtcNow
        );
        await db.SaveChangesAsync(ct);

        return Result.Success(new InstalledPlatformTemplateDto(Kind, row.Id, row.Name));
    }

    private Result Validate(TimerTemplatePayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Name))
            return Result.Failure("A timer template needs a name.", "VALIDATION_FAILED");

        if (payload.Name.Trim().Length > MaxNameLength)
            return Result.Failure(
                $"The timer name is longer than {MaxNameLength} characters.",
                "VALIDATION_FAILED"
            );

        if (payload.IntervalMinutes is < 1 or > MaxIntervalMinutes)
            return Result.Failure(
                $"The interval must be between 1 and {MaxIntervalMinutes} minutes.",
                "VALIDATION_FAILED"
            );

        if (payload.MinChatActivity is < 0 or > MaxMinChatActivity)
            return Result.Failure(
                $"The chat activity threshold must be between 0 and {MaxMinChatActivity}.",
                "VALIDATION_FAILED"
            );

        if (payload.Messages.Any(string.IsNullOrWhiteSpace))
            return Result.Failure("A timer message cannot be empty.", "VALIDATION_FAILED");

        foreach (string message in payload.Messages)
        {
            Result helperOk = templateHelperValidator.Validate(
                message,
                TemplateHelperContext.Timer
            );
            if (helperOk.IsFailure)
                return helperOk;
        }

        return Result.Success();
    }
}
