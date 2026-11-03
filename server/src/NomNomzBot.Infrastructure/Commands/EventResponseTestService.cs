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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Commands.TestFire;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// <see cref="IEventResponseTestService"/>: builds the sample event, then runs it through the REAL
/// <see cref="EventResponseExecutor"/> by its replay entry point (<see cref="IEventResponseExecutor.ReplayAsync"/>),
/// the same presentation-only path an activity replay uses. The executor's chat, overlay and pipeline seams are
/// wrapped by recorders so the result can say what was presented. This type reaches no currency, loyalty,
/// reward, counter, stats, hype-train, event-bus or journal service, so a test fire cannot move any of them.
/// </summary>
public sealed class EventResponseTestService : IEventResponseTestService
{
    private readonly IApplicationDbContext _db;
    private readonly IPipelineEngine _pipeline;
    private readonly ITemplateResolver _templates;
    private readonly IReplyDispatcher _replies;
    private readonly IOutboundSanctionAccessor _sanctions;
    private readonly IEventResponseOverlayNotifier _overlayNotifier;
    private readonly ITtsDispatchService _tts;
    private readonly ICommandInvoker _invoker;
    private readonly IOverlayPresenceRegistry _presence;
    private readonly ILogger<EventResponseExecutor> _logger;

    public EventResponseTestService(
        IApplicationDbContext db,
        IPipelineEngine pipeline,
        ITemplateResolver templates,
        IReplyDispatcher replies,
        IOutboundSanctionAccessor sanctions,
        IEventResponseOverlayNotifier overlayNotifier,
        ITtsDispatchService tts,
        ICommandInvoker invoker,
        IOverlayPresenceRegistry presence,
        ILogger<EventResponseExecutor> logger
    )
    {
        _db = db;
        _pipeline = pipeline;
        _templates = templates;
        _replies = replies;
        _sanctions = sanctions;
        _overlayNotifier = overlayNotifier;
        _tts = tts;
        _invoker = invoker;
        _presence = presence;
        _logger = logger;
    }

    public async Task<Result<EventResponseTestResult>> FireAsync(
        Guid broadcasterId,
        string eventType,
        Guid callerUserId,
        CancellationToken ct = default
    )
    {
        User? caller = await _db
            .Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == callerUserId, ct);
        if (caller is null)
            return Result.Failure<EventResponseTestResult>(
                "The caller was not found.",
                "NOT_FOUND"
            );

        EventResponseTestActor actor = new(
            caller.TwitchUserId ?? caller.Id.ToString(),
            caller.Username,
            caller.DisplayName
        );
        Dictionary<string, string>? sample = EventResponseTestSamples.For(eventType, actor);
        if (sample is null)
            return Result.Failure<EventResponseTestResult>(
                $"'{eventType}' is not an event type with a response.",
                "NOT_FOUND"
            );
        UseRealAlsoSaidClause(sample);

        List<string> warnings = [];
        if (!_presence.IsOverlayConnected(broadcasterId))
            warnings.Add(EventResponseTestWarnings.NoOverlaySource);

        EventResponse? row = await _db.EventResponses.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.EventType == eventType,
            ct
        );
        if (!await IsEnabledAsync(row, ct))
        {
            warnings.Add(EventResponseTestWarnings.ResponseDisabled);
            return Result.Success(NothingPresented(eventType, sample, warnings));
        }

        if (await LacksPipelineAsync(row!, ct))
        {
            warnings.Add(EventResponseTestWarnings.NoPipeline);
            return Result.Success(NothingPresented(eventType, sample, warnings));
        }

        return Result.Success(
            await RunAsync(broadcasterId, eventType, actor, sample, warnings, ct)
        );
    }

    private async Task<EventResponseTestResult> RunAsync(
        Guid broadcasterId,
        string eventType,
        EventResponseTestActor actor,
        Dictionary<string, string> sample,
        List<string> warnings,
        CancellationToken ct
    )
    {
        RecordingPipelineEngine engine = new(_pipeline);
        RecordingReplyDispatcher replies = new(_replies);
        RecordingOverlayNotifier overlay = new(_overlayNotifier);
        RecordingTtsDispatch tts = new(_tts);
        EventResponseExecutor executor = new(
            _db,
            engine,
            _templates,
            replies,
            _sanctions,
            overlay,
            tts,
            _invoker,
            _logger
        );

        EventResponseOutcome outcome = await executor.ReplayAsync(
            broadcasterId,
            eventType,
            actor.UserId,
            actor.DisplayName,
            sample,
            ct
        );

        return Presented(
            eventType,
            sample,
            replies.DeliveredLines.FirstOrDefault(),
            overlay.Alerts,
            engine.Steps,
            outcome,
            warnings,
            [.. tts.Lines, .. PlayTtsStepLines(engine.Steps)]
        );
    }

    /// <summary>
    /// A play_tts step calls the speech service itself, out of reach of the recorder, so its step log is the record:
    /// the text is not known, and a failed step carries the refusal.
    /// </summary>
    private static IEnumerable<EventResponseTestSpeechLine> PlayTtsStepLines(
        IReadOnlyList<StepExecutionLog> steps
    ) =>
        steps
            .Where(s => s.ActionType == "play_tts")
            .Select(s => new EventResponseTestSpeechLine(
                null,
                s.Succeeded ? null : s.ErrorMessage ?? "The speech step failed."
            ));

    /// <summary>A real event carries <c>{also_said}</c> as the ready-made clause, not the bare message the sample table holds.</summary>
    private static void UseRealAlsoSaidClause(Dictionary<string, string> sample)
    {
        if (!sample.ContainsKey("also_said"))
            return;

        sample.TryGetValue("message", out string? message);
        sample["also_said"] = AlsoSaidClause.From(message);
    }

    /// <summary>The result of a fire that presented nothing: only the sample and the warnings.</summary>
    private static EventResponseTestResult NothingPresented(
        string eventType,
        Dictionary<string, string> sample,
        List<string> warnings
    ) => new(eventType, sample, null, null, [], [], 0, 0, 0, warnings, []);

    private static EventResponseTestResult Presented(
        string eventType,
        Dictionary<string, string> sample,
        string? chatLine,
        IReadOnlyList<string> alerts,
        IReadOnlyList<StepExecutionLog> steps,
        EventResponseOutcome outcome,
        List<string> warnings,
        IReadOnlyList<EventResponseTestSpeechLine> speechLines
    ) =>
        new(
            eventType,
            sample,
            chatLine,
            outcome.TtsQueued > 0 ? chatLine : null,
            alerts,
            [
                .. steps.Select(s => new EventResponseTestStep(
                    s.StepIndex,
                    s.ActionType,
                    s.Succeeded,
                    s.ErrorMessage,
                    s.Output
                )),
            ],
            outcome.ChatMessagesSent,
            outcome.TtsQueued,
            outcome.OverlaysShown,
            warnings,
            speechLines
        );

    /// <summary>A channel's own row is enabled by its flag; a row that follows the platform default is as enabled as that default.</summary>
    private async Task<bool> IsEnabledAsync(EventResponse? row, CancellationToken ct)
    {
        if (row is null)
            return false;
        if (!row.FollowsPlatformDefault)
            return row.IsEnabled;

        PlatformEventResponseDefault? platform =
            await _db.PlatformEventResponseDefaults.FirstOrDefaultAsync(
                d => d.EventType == row.EventType,
                ct
            );
        return platform is { IsEnabled: true };
    }

    /// <summary>True when a pipeline response has nothing to run: no bound pipeline, or one that is missing or disabled.</summary>
    private async Task<bool> LacksPipelineAsync(EventResponse row, CancellationToken ct)
    {
        if (row.FollowsPlatformDefault || row.ResponseType != "pipeline")
            return false;
        if (row.GetBindingTarget() is { Kind: not BindingTargetKind.Pipeline })
            return false;
        if (row.PipelineId is not Guid pipelineId)
            return true;

        return !await _db.Pipelines.AnyAsync(p => p.Id == pipelineId && p.IsEnabled, ct);
    }
}
