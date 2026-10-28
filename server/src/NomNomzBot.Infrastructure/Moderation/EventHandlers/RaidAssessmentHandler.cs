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
