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

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Tts.PipelineActions;
using Timer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Commands.Jobs;

/// <summary>
/// Background service that manages per-channel timers (commands-pipelines.md §I.1).
///
/// Each timer has:
///   - IntervalMinutes: how often to fire
///   - MinChatActivity: minimum new chat messages since last fire before the timer will fire again
///   - Messages: round-robin list — the chat lines to send, OR (for a pipeline timer) the rotation
///     entries fed to the pipeline as <c>{timer.message}</c>
///   - PipelineId: when set, the timer dispatches that pipeline instead of sending a chat message —
///     e.g. rotating auto-shoutouts bind a pipeline whose action is <c>shoutout(user_id="{timer.message}")</c>
///     over a curated list of channel names.
///
/// The service polls every 30 seconds, checking whether any timer is due.
/// Timer state (last fired, rotation index) is persisted back to the database.
/// </summary>
public sealed class TimerService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly ITemplateResolver _templateResolver;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TimerService> _logger;

    // Per-timer in-memory state: tracks message count at last fire for activity gating
    // Key: timer.Id, Value: MessageCount snapshot when the timer last fired
    private readonly ConcurrentDictionary<Guid, long> _messageCountAtLastFire = new();

    public TimerService(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        ITemplateResolver templateResolver,
        TimeProvider timeProvider,
        ILogger<TimerService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _templateResolver = templateResolver;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TimerService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // The delay MUST still run on a throwing tick — otherwise a persistent failure spins the
                    // loop hot with zero backoff, hammering whatever just failed.
                    _logger.LogError(ex, "TimerService tick failed");
                }

                await Task.Delay(TickInterval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Internal (not private) so TimerServiceTests can drive a single deterministic tick —
    // InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam.
    internal async Task TickAsync(CancellationToken ct)
    {
        // Only process channels the bot is actively connected to
        IReadOnlyCollection<ChannelContext> liveChannels = _registry.GetAll();
        if (liveChannels.Count == 0)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();

        // Zero-downtime deploys deliberately overlap two instances; without a lease both would send
        // every due timer's message, doubling it in chat. Only the lease holder ticks — a non-holder is
        // not a fault (a deploy overlap is normal), so it does nothing and waits for the next tick.
        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync("timers", LeaseTtl, ct);
        if (lease is null)
            return;

        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        IPipelineEngine pipelineEngine =
            scope.ServiceProvider.GetRequiredService<IPipelineEngine>();

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        List<Guid> broadcasterIds = [.. liveChannels.Select(c => c.BroadcasterId)];

        List<Timer> timers = await db
            .Timers.Where(t =>
                t.IsEnabled && t.DeletedAt == null && broadcasterIds.Contains(t.BroadcasterId)
            )
            .ToListAsync(ct);

        foreach (Timer timer in timers)
        {
            try
            {
                await ProcessTimerAsync(scope.ServiceProvider, db, pipelineEngine, timer, now, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Timer {TimerId} ({Name}) failed", timer.Id, timer.Name);
            }
        }
    }

    private async Task ProcessTimerAsync(
        IServiceProvider services,
        IApplicationDbContext db,
        IPipelineEngine pipelineEngine,
        Timer timer,
        DateTime now,
        CancellationToken ct
    )
    {
        // Check interval
        DateTime nextFire = (timer.LastFiredAt ?? DateTime.MinValue).AddMinutes(
            timer.IntervalMinutes
        );
        if (now < nextFire)
            return;

        ChannelContext? channelCtx = _registry.Get(timer.BroadcasterId);
        if (channelCtx is null)
            return;

        // Check minimum chat activity since last fire
        if (timer.MinChatActivity > 0)
        {
            long countAtLastFire = _messageCountAtLastFire.TryGetValue(timer.Id, out long snap)
                ? snap
                : 0L;
            long messagesSinceLastFire = channelCtx.MessageCount - countAtLastFire;
            if (messagesSinceLastFire < timer.MinChatActivity)
            {
                _logger.LogDebug(
                    "Timer {Name} skipped — only {Count}/{Required} messages since last fire",
                    timer.Name,
                    messagesSinceLastFire,
                    timer.MinChatActivity
                );
                return;
            }
        }

        bool fired;
        if (
            timer.GetBindingTarget() is
            { Kind: BindingTargetKind.Command or BindingTargetKind.Action } target
        )
            fired = await FireTargetAsync(services, timer, target, ct);
        else
            fired = timer.PipelineId is { } pipelineId
                ? await FirePipelineAsync(db, pipelineEngine, timer, pipelineId, channelCtx, ct)
                : await FireMessageAsync(services, pipelineEngine, timer, channelCtx, ct);
        if (!fired)
            return;

        // Persist state: LastFiredAt + the shared round-robin advance (a pipeline timer walks the same
        // rotation — its list entries ride as {timer.message}).
        timer.LastFiredAt = now;
        if (timer.Messages.Count > 0)
            timer.NextMessageIndex = (timer.NextMessageIndex + 1) % timer.Messages.Count;
        // One-shot timers fire exactly once, then disable themselves so the next tick skips them.
        if (timer.FireOnce)
            timer.IsEnabled = false;
        await db.SaveChangesAsync(ct);

        // Record message count snapshot for next activity check
        _messageCountAtLastFire[timer.Id] = channelCtx.MessageCount;
    }

    /// <summary>
    /// A timer bound straight to a command or an action runs it through the one invoker, no wrapper pipeline.
    /// Always true (the attempt stamps <c>LastFiredAt</c>): a failed run retries on the next interval, never
    /// in a 30-second error loop.
    /// </summary>
    private async Task<bool> FireTargetAsync(
        IServiceProvider services,
        Timer timer,
        BindingTarget target,
        CancellationToken ct
    )
    {
        ICommandInvoker invoker = services.GetRequiredService<ICommandInvoker>();
        Result<InvocationOutcome> outcome = await invoker.InvokeAsync(
            timer.BroadcasterId,
            target,
            new(null, null, string.Empty, "timer", Variables: BuildVariables(timer)),
            ct
        );
        if (outcome.IsFailure)
            _logger.LogWarning(
                "Timer {Name} could not run its {Kind} target in channel {BroadcasterId}: {Error}",
                timer.Name,
                target.Kind,
                timer.BroadcasterId,
                outcome.ErrorMessage
            );
        return true;
    }

    /// <summary>The classic timer: sends the next round-robin chat line. True when a line was sent.</summary>
    private async Task<bool> FireMessageAsync(
        IServiceProvider services,
        IPipelineEngine pipelineEngine,
        Timer timer,
        ChannelContext channelCtx,
        CancellationToken ct
    )
    {
        if (timer.Messages.Count == 0)
            return false;

        // Pick next message (round-robin)
        string messageTemplate = timer.Messages[timer.NextMessageIndex % timer.Messages.Count];

        // The timer says where its line goes (chat, the chat overlay, both or neither); a timer that sets
        // nothing sends to chat as it always did. Template resolution is keyed by the tenant Guid.
        ReplyDelivery delivery = timer.ReplyDelivery ?? ReplyDelivery.ChatOnly;
        RenderedReply rendered = await ReplyTemplateRenderer.RenderAsync(
            _templateResolver,
            messageTemplate,
            new Dictionary<string, string>(),
            timer.BroadcasterId,
            delivery,
            ct
        );
        string message = rendered.ChatText;

        // A timer aimed nowhere has no text to check; every other target needs a line to send.
        if (
            delivery.Target != ReplyTarget.None
            && string.IsNullOrWhiteSpace(rendered.ChatText)
            && string.IsNullOrWhiteSpace(rendered.OverlayText)
        )
            return false;

        // A timer is background work with no viewer behind it: the channel's own timer configuration is what
        // sanctions the write. The outcome carries the real delivery result — before the S008d fix the send's
        // bool was discarded and true hardcoded, so a failed send still stamped LastFiredAt and advanced the
        // round-robin as if the line went out; the failed line was skipped forever instead of retrying.
        ReplyOutcome outcome;
        using (
            services
                .GetRequiredService<IOutboundSanctionAccessor>()
                .Begin(OutboundSanction.ChannelConfiguration($"timer:{timer.Name}"))
        )
            outcome = await services
                .GetRequiredService<IReplyDispatcher>()
                .DispatchAsync(
                    new ReplyRequest
                    {
                        BroadcasterId = channelCtx.BroadcasterId,
                        Text = rendered.ChatText,
                        OverlayText = rendered.OverlayText,
                        Delivery = delivery,
                    },
                    ct
                );
        if (!outcome.Succeeded(delivery))
        {
            _logger.LogWarning(
                "Timer {Name} failed to send in channel {BroadcasterId}: \"{Message}\"",
                timer.Name,
                timer.BroadcasterId,
                rendered.ChatText.Length > 0 ? rendered.ChatText : rendered.OverlayText
            );
            return false;
        }

        if (timer.SpeakWithTts && outcome.SentToChat)
            await ReplyTtsPipeline.QueueAsync(
                pipelineEngine,
                timer.BroadcasterId,
                channelCtx.TwitchChannelId,
                channelCtx.ChannelName,
                null,
                message,
                ct
            );

        _logger.LogInformation(
            "Timer {Name} fired in channel {BroadcasterId}: \"{Message}\"",
            timer.Name,
            timer.BroadcasterId,
            message
        );
        return true;
    }

    /// <summary>The variables a firing timer hands its pipeline: its name and the current rotation entry.</summary>
    internal static Dictionary<string, string> BuildVariables(Timer timer)
    {
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["timer.name"] = timer.Name,
        };
        if (timer.Messages.Count > 0)
            variables["timer.message"] = timer.Messages[
                timer.NextMessageIndex % timer.Messages.Count
            ];
        return variables;
    }

    /// <summary>
    /// The pipeline timer (spec §I.1's second dispatch leg): executes the bound pipeline with the current
    /// rotation entry as <c>{timer.message}</c> — how rotating auto-shoutouts walk a curated list. Always
    /// true (the attempt stamps <c>LastFiredAt</c>): a missing/deleted graph or a failed run retries on the
    /// next interval, never in a 30-second error loop.
    /// </summary>
    private async Task<bool> FirePipelineAsync(
        IApplicationDbContext db,
        IPipelineEngine pipelineEngine,
        Timer timer,
        Guid pipelineId,
        ChannelContext channelCtx,
        CancellationToken ct
    )
    {
        var pipeline = await db
            .Pipelines.Where(p =>
                p.Id == pipelineId && p.BroadcasterId == timer.BroadcasterId && p.DeletedAt == null
            )
            .Select(p => new { p.GraphJsonCache, p.IsEnabled })
            .FirstOrDefaultAsync(ct);

        if (pipeline is not null && !pipeline.IsEnabled)
        {
            _logger.LogDebug(
                "Timer {Name} points at disabled pipeline {PipelineId} — skipping this fire",
                timer.Name,
                pipelineId
            );
            return true;
        }

        string? graphJson = pipeline?.GraphJsonCache;
        if (string.IsNullOrEmpty(graphJson))
        {
            _logger.LogWarning(
                "Timer {Name} points at pipeline {PipelineId} with no executable graph — skipping this fire",
                timer.Name,
                pipelineId
            );
            return true;
        }

        Dictionary<string, string> variables = BuildVariables(timer);

        PipelineRequest request = new()
        {
            BroadcasterId = timer.BroadcasterId,
            // Without PipelineId the engine can never load live PipelineStep rows for this run, so it
            // falls back to its flat-only JSON path — a timer's bound pipeline would silently lose any
            // block-kind step (if/switch/loop/random_branch/try/detached_step) it carries.
            PipelineId = pipelineId,
            PipelineJson = graphJson,
            // A timer has no triggering chatter — the channel itself is the actor.
            TriggeredByUserId = channelCtx.TwitchChannelId,
            TriggeredByDisplayName = channelCtx.ChannelName,
            MessageId = string.Empty,
            RawMessage = string.Empty,
            InitialVariables = variables,
            SpeakWithTts = timer.SpeakWithTts,
        };

        PipelineExecutionResult result = await pipelineEngine.ExecuteAsync(request, ct);
        _logger.LogInformation(
            "Timer {Name} dispatched pipeline {PipelineId} in channel {BroadcasterId}: {Outcome}",
            timer.Name,
            pipelineId,
            timer.BroadcasterId,
            result.Outcome
        );
        return true;
    }
}
