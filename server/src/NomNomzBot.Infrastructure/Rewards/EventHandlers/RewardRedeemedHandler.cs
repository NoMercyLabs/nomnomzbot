// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// Handles channel point reward redemptions.
/// Looks up the Reward entity by its Twitch reward ID and executes the
/// configured PipelineJson. If the reward has a simple Response text,
/// that is used directly. With no reward-specific behavior, the generic
/// <c>channel.channel_points_custom_reward_redemption.add</c> event response
/// runs through the shared executor.
///
/// It is also the presentation half for a dashboard replay (<see cref="IEventResponsePresenter"/>): a replay runs
/// the same reward behavior again — the bound pipeline or target, the spoken input, the generic response — but
/// not the parts that belong to the one real redemption: the permission refund, the countdown timer, and the
/// after-processed event.
/// </summary>
public sealed class RewardRedeemedHandler
    : IEventHandler<RewardRedeemedEvent>,
        IEventResponsePresenter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPipelineEngine _pipeline;
    private readonly IEventBus _eventBus;
    private readonly ILogger<RewardRedeemedHandler> _logger;

    public RewardRedeemedHandler(
        IServiceScopeFactory scopeFactory,
        IPipelineEngine pipeline,
        IEventBus eventBus,
        ILogger<RewardRedeemedHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _pipeline = pipeline;
        _eventBus = eventBus;
        _logger = logger;
    }

    public Type EventType => typeof(RewardRedeemedEvent);

    public async Task HandleAsync(
        RewardRedeemedEvent @event,
        CancellationToken cancellationToken = default
    ) => await RunAsync(@event, replay: false, cancellationToken);

    public async Task<EventResponseOutcome> ReplayAsync(
        IDomainEvent @event,
        CancellationToken ct
    ) =>
        @event is RewardRedeemedEvent redemption
            ? await RunAsync(redemption, replay: true, ct)
            : EventResponseOutcome.None;

    private async Task<EventResponseOutcome> RunAsync(
        RewardRedeemedEvent @event,
        bool replay,
        CancellationToken cancellationToken
    )
    {
        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty)
            return EventResponseOutcome.None;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Look up Reward entity matched by TwitchRewardId
        Reward? reward = await db.Rewards.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.TwitchRewardId == @event.RewardId,
            cancellationToken
        );

        // A managed reward the broadcaster disabled locally runs none of its bot-side behavior — no
        // timer, no bound pipeline, no Response text, no generic event response — even though Twitch
        // itself already granted the redemption (Twitch's own pause/enable state is IsPaused, separate
        // from this local kill switch). An unmanaged/unrecognized reward (reward is null) is unaffected.
        if (reward is { IsEnabled: false })
        {
            _logger.LogDebug(
                "Reward {RewardId} in {Channel} is disabled — skipping redemption behavior",
                @event.RewardId,
                broadcasterId
            );
            return EventResponseOutcome.None;
        }

        // A viewer below the reward's permission is refunded and told so; nothing of the reward runs
        // (legacy parity: no timer, no pipeline, no generic response). A replay makes the same call but
        // never refunds: the redemption is long settled, and a refused one must stay refused.
        if (
            reward is not null
            && (
                replay
                    ? !await RewardPermissionGate.MeetsPermissionAsync(
                        scope.ServiceProvider,
                        db,
                        reward,
                        @event,
                        cancellationToken
                    )
                    : await RewardPermissionGate.TryRefuseAsync(
                        scope.ServiceProvider,
                        db,
                        reward,
                        @event,
                        _logger,
                        cancellationToken
                    )
            )
        )
            return EventResponseOutcome.None;

        // The viewer's own text, resolved once for every use below: the spoken line, the {input} variable and the
        // text a bound command receives. It stays as typed unless the channel and the viewer allow templates.
        @event = await WithResolvedInputAsync(scope, @event, reward, replay, cancellationToken);
        Dictionary<string, string> variables = BuildVariables(@event);

        // A time-limited reward starts its countdown the moment it's redeemed (idempotent per
        // redemption — an EventSub redelivery returns the existing timer). Orthogonal to the response, and
        // not part of a replay: the one real redemption owns its timer.
        if (!replay && reward?.TimerDurationSeconds is { } timerSeconds and > 0)
        {
            IRedemptionTimerService timers =
                scope.ServiceProvider.GetRequiredService<IRedemptionTimerService>();
            Result<RedemptionTimerDto> started = await timers.StartAsync(
                broadcasterId,
                @event.RedemptionId,
                @event.RewardId,
                @event.RewardTitle,
                @event.UserDisplayName,
                timerSeconds,
                cancellationToken
            );
            if (started.IsFailure)
                _logger.LogWarning(
                    "Redemption timer failed to start for {RedemptionId} in {Channel}: {Error}",
                    @event.RedemptionId,
                    broadcasterId,
                    started.ErrorMessage
                );
        }

        // A reward that speaks reads the viewer's text aloud in their own voice. Orthogonal to the response.
        EventResponseOutcome outcome = EventResponseOutcome.None;
        if (reward is { SpeaksViaTts: true } && !string.IsNullOrWhiteSpace(@event.UserInput))
        {
            bool spoken = await SpeakRedemptionTextAsync(
                scope,
                db,
                broadcasterId,
                @event,
                cancellationToken
            );
            outcome = new(0, spoken ? 1 : 0, 0);
        }

        // A reward bound straight to a command or an action runs it through the one invoker: no wrapper pipeline.
        // A pipeline target (or none) keeps the path below unchanged.
        if (
            reward?.GetBindingTarget() is
            { Kind: BindingTargetKind.Command or BindingTargetKind.Action } target
        )
        {
            bool targetRan = await RunDirectTargetAsync(
                scope,
                broadcasterId,
                target,
                @event,
                variables,
                replay,
                cancellationToken
            );
            return outcome.Plus(RewardReplayTally.FromDirectRun(targetRan));
        }

        string? pipelineJson = reward?.PipelineJson;
        // Set only when pipelineJson below actually came from this bound pipeline's own graph — never for
        // the inline reward.PipelineJson or the synthetic BuildResponsePipeline fallback, neither of which
        // has real PipelineStep rows behind it. Required so the engine dispatches this reward's block-kind
        // steps (if/switch/loop/random_branch/try/detached_step) through its tree walker instead of its
        // flat-only JSON fallback.
        Guid? pipelineId = null;

        // A reward can bind a SAVED pipeline (the reward analogue of a timer's PipelineId): load its compiled
        // graph and run that — the path a reward-triggered play_sound takes. Takes precedence over the inline
        // PipelineJson / Response fallbacks. A binding whose graph is missing/deleted degrades to those.
        if (reward?.PipelineId is { } boundPipelineId)
        {
            string? graphJson = await db
                .Pipelines.Where(p =>
                    p.Id == boundPipelineId
                    && p.BroadcasterId == broadcasterId
                    && p.DeletedAt == null
                )
                .Select(p => p.GraphJsonCache)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrEmpty(graphJson))
            {
                pipelineJson = graphJson;
                pipelineId = boundPipelineId;
            }
            else
                _logger.LogWarning(
                    "Reward {RewardId} in {Channel} binds pipeline {PipelineId} with no executable graph — falling back",
                    @event.RewardId,
                    broadcasterId,
                    boundPipelineId
                );
        }

        // Fall back to simple Response text as a send_message pipeline
        if (pipelineJson is null && reward?.Response is not null)
        {
            pipelineJson = BuildResponsePipeline(reward.Response, reward.ReplyDelivery);
        }

        // No reward-specific behavior → the generic redemption event response (the row the
        // event-responses page edits), through the shared executor like every other trigger source.
        if (string.IsNullOrWhiteSpace(pipelineJson))
            return outcome.Plus(
                await RunGenericResponseAsync(
                    scope,
                    broadcasterId,
                    @event,
                    variables,
                    replay,
                    cancellationToken
                )
            );

        _logger.LogDebug(
            "Executing pipeline for reward {RewardId} in channel {Channel}",
            @event.RewardId,
            broadcasterId
        );

        // A replay hands the run no activity-row id: the utterance a replayed play_tts queues must not be
        // captured under the original redemption a second time.
        PipelineExecutionResult? run = await ExecutePipelineAsync(
            broadcasterId,
            pipelineId,
            pipelineJson,
            @event,
            replay ? null : @event.EventId.ToString(),
            variables,
            replay,
            cancellationToken
        );
        return run is null ? outcome : outcome.Plus(RewardReplayTally.FromPipeline(run));
    }

    /// <summary>
    /// The redemption with its input resolved by <see cref="IViewerInputTemplateService"/>; the same event when there is
    /// no input, no service, or the text stays as typed.
    /// </summary>
    private static async Task<RewardRedeemedEvent> WithResolvedInputAsync(
        IServiceScope scope,
        RewardRedeemedEvent @event,
        Reward? reward,
        bool replay,
        CancellationToken ct
    )
    {
        IViewerInputTemplateService? templates =
            scope.ServiceProvider.GetService<IViewerInputTemplateService>();
        if (templates is null || string.IsNullOrWhiteSpace(@event.UserInput))
            return @event;

        string resolved = await templates.ResolveRewardInputAsync(
            @event.BroadcasterId,
            new ViewerInputIdentity(@event.UserId, @event.UserLogin, @event.UserDisplayName),
            @event.UserInput,
            reward is { SpeaksViaTts: true },
            audit: !replay,
            ct
        );
        return resolved == @event.UserInput
            ? @event
            : new RewardRedeemedEvent
            {
                EventId = @event.EventId,
                OccurredAt = @event.OccurredAt,
                BroadcasterId = @event.BroadcasterId,
                IsChannelIngress = @event.IsChannelIngress,
                RewardId = @event.RewardId,
                RewardTitle = @event.RewardTitle,
                RedemptionId = @event.RedemptionId,
                UserId = @event.UserId,
                UserDisplayName = @event.UserDisplayName,
                UserLogin = @event.UserLogin,
                Cost = @event.Cost,
                UserInput = resolved,
            };
    }

    private static async Task<EventResponseOutcome> RunGenericResponseAsync(
        IServiceScope scope,
        Guid broadcasterId,
        RewardRedeemedEvent @event,
        Dictionary<string, string> variables,
        bool replay,
        CancellationToken ct
    )
    {
        IEventResponseExecutor executor =
            scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();
        if (replay)
            return await executor.ReplayAsync(
                broadcasterId,
                RewardRedeemedSampleSource.ResponseKey,
                @event.UserId,
                @event.UserDisplayName,
                variables,
                ct
            );

        await executor.ExecuteAsync(
            broadcasterId,
            RewardRedeemedSampleSource.ResponseKey,
            @event.UserId,
            @event.UserDisplayName,
            variables,
            ct
        );
        return EventResponseOutcome.None;
    }

    private async Task<bool> SpeakRedemptionTextAsync(
        IServiceScope scope,
        IApplicationDbContext db,
        Guid broadcasterId,
        RewardRedeemedEvent @event,
        CancellationToken cancellationToken
    )
    {
        ITtsDispatchService? tts = scope.ServiceProvider.GetService<ITtsDispatchService>();
        if (tts is null)
            return false;

        Guid viewerUserId = await db
            .Users.Where(u => u.TwitchUserId == @event.UserId)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // The redemption already paid for the line, and Twitch has its own cooldown: the chat gates (bits floor,
        // role floor, per-viewer cooldown) do not apply.
        Result<TtsDispatchOutcome> spoken = await tts.RequestSpeakAsync(
            new TtsSpeakRequest(
                BroadcasterId: broadcasterId,
                RequestedByUserId: viewerUserId,
                RequestedByTwitchUserId: @event.UserId,
                RequestedByDisplayName: @event.UserDisplayName,
                Text: @event.UserInput!,
                VoiceIdOverride: null,
                BitsAmount: 0,
                CommunityStanding: "everyone",
                SourceMessageId: null,
                StreamId: null,
                Speaker: TtsSpeaker.Viewer,
                SpokenNames: [@event.UserDisplayName],
                AlreadyPaid: true
            ),
            cancellationToken
        );
        if (spoken.IsFailure)
            _logger.LogWarning(
                "Reward {RewardId} in {Channel} could not speak the redemption text: {Error}",
                @event.RewardId,
                broadcasterId,
                spoken.ErrorMessage
            );

        return spoken.IsSuccess;
    }

    /// <summary>Runs the bound command or action; true when it succeeded.</summary>
    private async Task<bool> RunDirectTargetAsync(
        IServiceScope scope,
        Guid broadcasterId,
        BindingTarget target,
        RewardRedeemedEvent @event,
        Dictionary<string, string> variables,
        bool replay,
        CancellationToken ct
    )
    {
        bool succeeded = false;
        long startedAt = Stopwatch.GetTimestamp();
        try
        {
            ICommandInvoker invoker = scope.ServiceProvider.GetRequiredService<ICommandInvoker>();
            Result<InvocationOutcome> run = await invoker.InvokeAsync(
                broadcasterId,
                target,
                new(
                    @event.UserId,
                    @event.UserDisplayName,
                    @event.UserInput ?? string.Empty,
                    "reward",
                    Variables: variables
                ),
                ct
            );
            succeeded = run is { IsSuccess: true, Value.Succeeded: true };
            if (!succeeded)
                _logger.LogWarning(
                    "Reward {RewardId} in {Channel} could not run its {Kind} target: {Error}",
                    @event.RewardId,
                    broadcasterId,
                    target.Kind,
                    run.IsFailure ? run.ErrorMessage : run.Value.Error
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run reward target in channel {Channel}", broadcasterId);
        }

        if (!replay)
            await PublishAfterProcessedAsync(
                broadcasterId,
                @event.RewardId,
                @event.RedemptionId,
                succeeded,
                Stopwatch.GetElapsedTime(startedAt),
                ct
            );

        return succeeded;
    }

    /// <summary>The variables a redemption hands its reward pipeline or event response.</summary>
    internal static Dictionary<string, string> BuildVariables(RewardRedeemedEvent @event)
    {
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["reward"] = @event.RewardTitle,
            ["reward.id"] = @event.RewardId,
            ["redemption.id"] = @event.RedemptionId,
            ["cost"] = @event.Cost.ToString(),
            ["input"] = @event.UserInput ?? string.Empty,
        };
        TemplateSubject.SeedUser(
            variables,
            @event.UserId,
            @event.UserLogin,
            @event.UserDisplayName
        );
        return variables;
    }

    /// <summary>
    /// The one-step <c>send_message</c> pipeline a reward's simple Response runs as. A reward that sets a
    /// <paramref name="delivery"/> hands it to the step as <c>reply_target</c> / <c>reply_html</c>, so the line
    /// goes where the reward says; a reward that sets none builds the same step it always did.
    /// </summary>
    internal static string BuildResponsePipeline(string message, ReplyDelivery? delivery)
    {
        string escaped = message.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string deliveryParams = delivery is null
            ? string.Empty
            : $",\"reply_target\":\"{delivery.Target.ToString().ToLowerInvariant()}\",\"reply_html\":{(delivery.Html ? "true" : "false")}";
        return "{\"steps\":[{\"action\":{\"type\":\"send_message\",\"message\":\""
            + escaped
            + "\",\"target\":\"channel\""
            + deliveryParams
            + "}}]}";
    }

    /// <summary>Runs the reward's pipeline; null when the run threw. A live run reports the result on the bus,
    /// a replay does not.</summary>
    private async Task<PipelineExecutionResult?> ExecutePipelineAsync(
        Guid broadcasterId,
        Guid? pipelineId,
        string pipelineJson,
        RewardRedeemedEvent redemption,
        string? channelEventId,
        Dictionary<string, string> variables,
        bool replay,
        CancellationToken ct
    )
    {
        PipelineExecutionResult? result = null;
        bool succeeded = false;
        TimeSpan duration;
        long startedAt = Stopwatch.GetTimestamp();
        try
        {
            result = await _pipeline.ExecuteAsync(
                new()
                {
                    BroadcasterId = broadcasterId,
                    // Only set when pipelineJson came from a real bound Pipeline row (see the caller) — lets
                    // the engine dispatch block-kind steps (if/switch/loop/random_branch/try/detached_step)
                    // through its tree walker instead of its flat-only JSON fallback.
                    PipelineId = pipelineId,
                    PipelineJson = pipelineJson,
                    TriggeredByUserId = redemption.UserId,
                    TriggeredByDisplayName = redemption.UserDisplayName,
                    RedemptionId = redemption.RedemptionId,
                    RewardId = redemption.RewardId,
                    // The redemption's own activity-feed row id — a reward's pipeline can include play_tts,
                    // and that utterance's dispatched event needs this to correlate its Replay capture back
                    // to the redemption (S-REPLAY-TTS-PIPELINE-CORRELATION).
                    ChannelEventId = channelEventId,
                    RawMessage = string.Empty,
                    InitialVariables = variables,
                },
                ct
            );
            succeeded = result.Outcome is PipelineOutcome.Completed or PipelineOutcome.Stopped;
            duration = result.Duration;
        }
        catch (Exception ex)
        {
            duration = Stopwatch.GetElapsedTime(startedAt);
            _logger.LogError(
                ex,
                "Failed to execute reward pipeline in channel {Channel}",
                broadcasterId
            );
        }

        if (!replay)
            await PublishAfterProcessedAsync(
                broadcasterId,
                redemption.RewardId,
                redemption.RedemptionId,
                succeeded,
                duration,
                ct
            );

        return result;
    }

    private async Task PublishAfterProcessedAsync(
        Guid broadcasterId,
        string rewardId,
        string redemptionId,
        bool succeeded,
        TimeSpan duration,
        CancellationToken ct
    )
    {
        try
        {
            await _eventBus.PublishAsync(
                new AfterRewardProcessedEvent
                {
                    BroadcasterId = broadcasterId,
                    RewardId = rewardId,
                    RedemptionId = redemptionId,
                    Succeeded = succeeded,
                    Duration = duration,
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish AfterRewardProcessedEvent for {RedemptionId} in channel {Channel}",
                redemptionId,
                broadcasterId
            );
        }
    }
}
