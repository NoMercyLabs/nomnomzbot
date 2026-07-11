// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Supporters.Events;

namespace NomNomzBot.Infrastructure.Supporters.EventHandlers;

/// <summary>
/// Fires the streamer's bound responses for a supporter event (supporter-events.md §4). One
/// <see cref="SupporterEventReceived"/> dispatches to TWO trigger keys — the specific
/// <c>supporter.&lt;kind&gt;</c> and the catch-all <c>supporter.any</c> — so a streamer can bind either or both.
/// A configured <c>EventResponse</c> (chat message or pipeline) runs with the supporter template vars, and the
/// event is logged to the activity feed. Unlike the Twitch alert handlers this dispatches two keys, so it runs
/// its own lookup rather than the single-key <c>TwitchAlertHandlerBase</c>.
/// </summary>
public sealed class SupporterTriggerSource : IEventHandler<SupporterEventReceived>
{
    private readonly IApplicationDbContext _db;
    private readonly IPipelineEngine _pipeline;
    private readonly ITemplateResolver _templateResolver;
    private readonly IChatProvider _chatProvider;
    private readonly ILogger<SupporterTriggerSource> _logger;

    public SupporterTriggerSource(
        IApplicationDbContext db,
        IPipelineEngine pipeline,
        ITemplateResolver templateResolver,
        IChatProvider chatProvider,
        ILogger<SupporterTriggerSource> logger
    )
    {
        _db = db;
        _pipeline = pipeline;
        _templateResolver = templateResolver;
        _chatProvider = chatProvider;
        _logger = logger;
    }

    public async Task HandleAsync(
        SupporterEventReceived @event,
        CancellationToken cancellationToken = default
    )
    {
        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty)
            return;

        Dictionary<string, string> variables = BuildVariables(@event);
        await LogActivityAsync(@event, variables, cancellationToken);

        // Specific first, then the catch-all — a streamer may bind either, both, or neither.
        string kindKey = $"supporter.{@event.Kind}";
        await RunBoundResponseAsync(broadcasterId, kindKey, variables, cancellationToken);
        await RunBoundResponseAsync(broadcasterId, "supporter.any", variables, cancellationToken);
    }

    private static Dictionary<string, string> BuildVariables(SupporterEventReceived e) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = e.SupporterDisplayName,
            ["supporter.name"] = e.SupporterDisplayName,
            ["supporter.kind"] = e.Kind,
            ["supporter.amount"] = FormatAmount(e.AmountMinor),
            ["supporter.currency"] = e.Currency ?? string.Empty,
            ["supporter.tier"] = e.Tier ?? string.Empty,
            ["supporter.quantity"] =
                e.Quantity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["supporter.message"] = e.MessageText ?? string.Empty,
        };

    /// <summary>Minor units → a human "12.34" (or "" when there is no amount).</summary>
    private static string FormatAmount(long? amountMinor) =>
        amountMinor is long minor
            ? (minor / 100m).ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;

    private async Task RunBoundResponseAsync(
        Guid broadcasterId,
        string eventTypeKey,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        EventResponse? config = await _db.EventResponses.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.EventType == eventTypeKey && r.IsEnabled,
            ct
        );
        if (config is null)
            return;

        try
        {
            switch (config.ResponseType)
            {
                case "chat_message":
                    if (string.IsNullOrWhiteSpace(config.Message))
                        break;
                    string message = await _templateResolver.ResolveAsync(
                        config.Message,
                        variables,
                        broadcasterId,
                        ct
                    );
                    if (!string.IsNullOrWhiteSpace(message))
                        await _chatProvider.SendMessageAsync(broadcasterId, message, ct);
                    break;

                case "pipeline":
                    if (!config.PipelineId.HasValue)
                        break;
                    Pipeline? pipeline = await _db.Pipelines.FirstOrDefaultAsync(
                        p => p.Id == config.PipelineId.Value,
                        ct
                    );
                    if (pipeline is null)
                        break;
                    await _pipeline.ExecuteAsync(
                        new PipelineRequest
                        {
                            BroadcasterId = broadcasterId,
                            PipelineId = config.PipelineId,
                            PipelineJson = pipeline.GraphJsonCache ?? "{}",
                            TriggeredByUserId = string.Empty,
                            TriggeredByDisplayName = variables.GetValueOrDefault(
                                "supporter.name",
                                string.Empty
                            ),
                            RawMessage = string.Empty,
                            InitialVariables = variables,
                        },
                        ct
                    );
                    break;

                // "none"/"overlay" or unknown: no direct chat/pipeline action here.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to execute event_response:{EventType} ({ResponseType}) in {Channel}",
                eventTypeKey,
                config.ResponseType,
                broadcasterId
            );
        }
    }

    private async Task LogActivityAsync(
        SupporterEventReceived @event,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        try
        {
            _db.ChannelEvents.Add(
                new()
                {
                    Id = Ulid.NewUlid().ToString(),
                    ChannelId = @event.BroadcasterId,
                    UserId = @event.SupporterUserId,
                    Type = $"supporter.{@event.Kind}",
                    Data = JsonSerializer.Serialize(variables),
                }
            );
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to log supporter ChannelEvent for {Channel}",
                @event.BroadcasterId
            );
        }
    }
}
