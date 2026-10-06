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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// <see cref="IEventResponseExecutor"/> over the tenant's <see cref="EventResponse"/> rows:
/// <c>chat_message</c> resolves the operator's template against the trigger's variables and sends it via
/// the chat provider — and, when the response opts in (<c>SpeakWithTts</c>), hands the same resolved text
/// to the channel's TTS, whose failure never holds back the chat message; <c>overlay</c> resolves the same template and pushes it (plus the operator's metadata)
/// to the broadcaster's overlay clients — it never also posts to chat; <c>pipeline</c> runs the bound
/// pipeline's cached graph with the variables seeded; <c>none</c> (or a disabled/absent row) does nothing.
/// Scoped — trigger sources resolve it from their own scope (hosted-service handlers) or take it by
/// constructor (already-scoped handlers).
/// </summary>
public sealed class EventResponseExecutor : IEventResponseExecutor
{
    private readonly IApplicationDbContext _db;
    private readonly IPipelineEngine _pipeline;
    private readonly ITemplateResolver _templateResolver;
    private readonly IChatProvider _chatProvider;
    private readonly IEventResponseOverlayNotifier _overlayNotifier;
    private readonly ITtsDispatchService _tts;
    private readonly ILogger<EventResponseExecutor> _logger;

    public EventResponseExecutor(
        IApplicationDbContext db,
        IPipelineEngine pipeline,
        ITemplateResolver templateResolver,
        IChatProvider chatProvider,
        IEventResponseOverlayNotifier overlayNotifier,
        ITtsDispatchService tts,
        ILogger<EventResponseExecutor> logger
    )
    {
        _db = db;
        _pipeline = pipeline;
        _templateResolver = templateResolver;
        _chatProvider = chatProvider;
        _overlayNotifier = overlayNotifier;
        _tts = tts;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string? userId,
        string? userDisplayName,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken = default
    )
    {
        await ResumeWaitingRunsAsync(broadcasterId, eventTypeKey, variables, cancellationToken);
        await RespondAsync(
            broadcasterId,
            eventTypeKey,
            userId,
            userDisplayName,
            variables,
            cancellationToken
        );
    }

    // A replay is the same response, minus waking parked runs: the event is not happening again.
    public Task<EventResponseOutcome> ReplayAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string? userId,
        string? userDisplayName,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken = default
    ) =>
        RespondAsync(
            broadcasterId,
            eventTypeKey,
            userId,
            userDisplayName,
            variables,
            cancellationToken
        );

    // S-PIPE-TREE-d3c: this is THE single choke point every trigger source (74 EventSub
    // translators, timers, rewards, supporters, …) already dispatches through with a real,
    // bus-delivered domain event behind it — so it is also the one place a `wait_for_event`
    // pipeline step can be resumed without inventing a second event-fan-out mechanism. Runs
    // UNCONDITIONALLY, before the EventResponse lookup, because a channel can have a run
    // parked on this event name with no EventResponse row configured for it at all. Failures
    // here must never block the configured response or escape into the event bus.
    private async Task ResumeWaitingRunsAsync(
        Guid broadcasterId,
        string eventTypeKey,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _pipeline.ResumeSuspendedRunsForEventAsync(
                broadcasterId,
                eventTypeKey,
                variables,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to resume wait_for_event runs for {EventType} in {Channel}",
                eventTypeKey,
                broadcasterId
            );
        }
    }

    private async Task<EventResponseOutcome> RespondAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string? userId,
        string? userDisplayName,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken
    )
    {
        EventResponse? row = await _db.EventResponses.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.EventType == eventTypeKey,
            cancellationToken
        );
        EffectiveResponse? config = row switch
        {
            null => null,
            { FollowsPlatformDefault: true } => await PlatformDefaultAsync(
                broadcasterId,
                row.EventType,
                cancellationToken
            ),
            _ => new(
                row.IsEnabled,
                row.ResponseType,
                await OwnMessageAsync(row, cancellationToken),
                row.PipelineId,
                row.MetadataJson,
                row.SpeakWithTts
            ),
        };
        if (config is not { IsEnabled: true })
            return EventResponseOutcome.None;

        _logger.LogDebug(
            "Executing event response {EventType} ({ResponseType}) for channel {Channel}",
            eventTypeKey,
            config.ResponseType,
            broadcasterId
        );

        try
        {
            return config.ResponseType switch
            {
                "chat_message" => await SendChatMessageAsync(
                    broadcasterId,
                    eventTypeKey,
                    config.Message,
                    config.SpeakWithTts,
                    variables,
                    cancellationToken
                ),
                "pipeline" => await RunPipelineAsync(
                    broadcasterId,
                    eventTypeKey,
                    config.PipelineId,
                    userId,
                    userDisplayName,
                    variables,
                    cancellationToken
                ),
                "overlay" => await SendOverlayAsync(
                    broadcasterId,
                    eventTypeKey,
                    config.Message,
                    config.Metadata,
                    variables,
                    cancellationToken
                ),
                // "none" or any unknown type: no action.
                _ => EventResponseOutcome.None,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to execute event response {EventType} ({ResponseType}) in {Channel}",
                eventTypeKey,
                config.ResponseType,
                broadcasterId
            );
            return EventResponseOutcome.None;
        }
    }

    /// <summary>
    /// The response a channel that never chose its own gets: the platform default for the event type, as a
    /// chat message. The platform admin's text wins; without one the line is a random pick from the tone
    /// catalogue in the channel's personality tone. Null when no platform default exists for the type
    /// (nothing happens, as before).
    /// </summary>
    private async Task<EffectiveResponse?> PlatformDefaultAsync(
        Guid broadcasterId,
        string eventType,
        CancellationToken ct
    )
    {
        PlatformEventResponseDefault? platform =
            await _db.PlatformEventResponseDefaults.FirstOrDefaultAsync(
                d => d.EventType == eventType,
                ct
            );
        if (platform is null)
            return null;

        string? message =
            EventResponseToneCatalog.OwnText(eventType, platform.Message)
            ?? EventResponseToneCatalog.Pick(await PersonalityAsync(broadcasterId, ct), eventType);
        return new(platform.IsEnabled, "chat_message", message, null, [], platform.SpeakWithTts);
    }

    /// <summary>A chat row of its own with no text speaks a line in the channel's tone; any other row, its own text.</summary>
    private async Task<string?> OwnMessageAsync(EventResponse row, CancellationToken ct) =>
        row.ResponseType == "chat_message"
        && EventResponseToneCatalog.OwnText(row.EventType, row.Message) is null
            ? EventResponseToneCatalog.Pick(
                await PersonalityAsync(row.BroadcasterId, ct),
                row.EventType
            )
            : row.Message;

    private async Task<string?> PersonalityAsync(Guid broadcasterId, CancellationToken ct) =>
        await _db
            .Channels.AsNoTracking()
            .Where(c => c.Id == broadcasterId)
            .Select(c => c.Personality)
            .FirstOrDefaultAsync(ct);

    /// <summary>The response the runtime actually performs — the channel's own row or the platform default.</summary>
    private sealed record EffectiveResponse(
        bool IsEnabled,
        string ResponseType,
        string? Message,
        Guid? PipelineId,
        Dictionary<string, string> Metadata,
        bool SpeakWithTts
    );

    private async Task<EventResponseOutcome> SendChatMessageAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string? messageTemplate,
        bool speakWithTts,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(messageTemplate))
            return EventResponseOutcome.None;

        string message = await _templateResolver.ResolveAsync(
            messageTemplate,
            variables,
            broadcasterId,
            ct
        );
        if (string.IsNullOrWhiteSpace(message))
            return EventResponseOutcome.None;

        bool sent = await _chatProvider.SendMessageAsync(broadcasterId, message, ct);
        bool spoken =
            speakWithTts && await SpeakAsync(broadcasterId, eventTypeKey, message, variables, ct);
        return EventResponseOutcome.None with
        {
            ChatMessagesSent = sent ? 1 : 0,
            TtsQueued = spoken ? 1 : 0,
        };
    }

    /// <summary>
    /// Hands the resolved chat line to the channel's TTS. The line is the channel's own announcement, so it
    /// reads in the channel's default voice (no viewer named) with the broadcaster's standing — the same
    /// choice the shoutout announcement makes. Every other gate (TTS enabled, bits, character cap, censor,
    /// moderator approval) belongs to the dispatch service, exactly as for a pipeline's play_tts step. A
    /// rejection or an exception is logged and never escapes; whether anything could PLAY the line is
    /// reported by the tts_speak broadcaster, so nothing here claims it was heard.
    /// </summary>
    private async Task<bool> SpeakAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string text,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        try
        {
            Result<TtsDispatchOutcome> result = await _tts.RequestSpeakAsync(
                new(
                    BroadcasterId: broadcasterId,
                    RequestedByUserId: Guid.Empty,
                    RequestedByTwitchUserId: string.Empty,
                    RequestedByDisplayName: string.Empty,
                    Text: text,
                    VoiceIdOverride: null,
                    BitsAmount: variables.TryGetValue("user.bits", out string? bits)
                    && int.TryParse(bits, out int bitsAmount)
                        ? bitsAmount
                        : 0,
                    CommunityStanding: "broadcaster",
                    SourceMessageId: null,
                    StreamId: null,
                    SpokenNames: SpokenNameVariables.From(variables)
                ),
                ct
            );
            if (result.IsSuccess)
                return true;
            _logger.LogWarning(
                "Event response {EventType} in {Channel} went to chat but was not spoken: {Reason}",
                eventTypeKey,
                broadcasterId,
                result.ErrorMessage
            );
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Event response {EventType} in {Channel} failed to reach TTS after its chat message",
                eventTypeKey,
                broadcasterId
            );
            return false;
        }
    }

    private async Task<EventResponseOutcome> SendOverlayAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string? messageTemplate,
        Dictionary<string, string> metadata,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        string resolvedMessage = string.IsNullOrWhiteSpace(messageTemplate)
            ? string.Empty
            : await _templateResolver.ResolveAsync(messageTemplate, variables, broadcasterId, ct);

        await _overlayNotifier.NotifyAsync(
            broadcasterId,
            eventTypeKey,
            resolvedMessage,
            metadata,
            ct
        );
        return EventResponseOutcome.None with { OverlaysShown = 1 };
    }

    private async Task<EventResponseOutcome> RunPipelineAsync(
        Guid broadcasterId,
        string eventTypeKey,
        Guid? pipelineId,
        string? userId,
        string? userDisplayName,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        if (!pipelineId.HasValue)
            return EventResponseOutcome.None;

        Domain.Commands.Entities.Pipeline? pipeline = await _db.Pipelines.FirstOrDefaultAsync(
            p => p.Id == pipelineId.Value,
            ct
        );
        if (pipeline is null || !pipeline.IsEnabled)
            return EventResponseOutcome.None;

        // The run's steps can see which event started it (event.name, the same key a wait_for_event resume
        // uses), so a shoutout step can tell a raid from any other trigger. A copy: the caller's bag is shared.
        Dictionary<string, string> pipelineVariables = new(
            variables,
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["event.name"] = eventTypeKey,
        };

        PipelineExecutionResult result = await _pipeline.ExecuteAsync(
            new()
            {
                BroadcasterId = broadcasterId,
                PipelineId = pipelineId,
                PipelineJson = pipeline.GraphJsonCache ?? "{}",
                TriggeredByUserId = userId ?? string.Empty,
                TriggeredByDisplayName = userDisplayName ?? string.Empty,
                RawMessage = string.Empty,
                InitialVariables = pipelineVariables,
            },
            ct
        );

        // A pipeline's own chat steps are its business; what is counted here is the TTS it queued, so a replay
        // can say whether the stream heard anything.
        int ttsQueued = result.StepLogs.Count(log =>
            log is { ActionType: "play_tts", Succeeded: true }
        );
        return EventResponseOutcome.None with { TtsQueued = ttsQueued };
    }
}
