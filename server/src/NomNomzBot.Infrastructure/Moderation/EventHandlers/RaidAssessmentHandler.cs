// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>Vets the raider of every incoming raid and stores the result for the moderators.</summary>
public sealed class RaidAssessmentHandler : IEventHandler<RaidEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RaidAssessmentHandler> _logger;

    public RaidAssessmentHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<RaidAssessmentHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task HandleAsync(RaidEvent @event, CancellationToken ct)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            Result<RaidAssessmentDto> result = await scope
                .ServiceProvider.GetRequiredService<IRaidAssessmentService>()
                .AssessAsync(@event, ct);

            if (result.IsFailure)
                _logger.LogWarning(
                    "Raid from {Raider} in {Channel} was not assessed: {Error}",
                    @event.FromUserId,
                    @event.BroadcasterId,
                    result.ErrorMessage
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Raid assessment failed for raider {Raider} in {Channel}",
                @event.FromUserId,
                @event.BroadcasterId
            );
        }
    }
}
