// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
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
                await TickAsync(stoppingToken);
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TimerService tick failed");
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
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        IChatProvider chat = scope.ServiceProvider.GetRequiredService<IChatProvider>();
        IPipelineEngine pipelineEngine =
            scope.ServiceProvider.GetRequiredService<IPipelineEngine>();

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        List<Guid> broadcasterIds = liveChannels.Select(c => c.BroadcasterId).ToList();

        List<Timer> timers = await db
            .Timers.Where(t =>
                t.IsEnabled && t.DeletedAt == null && broadcasterIds.Contains(t.BroadcasterId)
            )
            .ToListAsync(ct);

        foreach (Timer timer in timers)
        {
            try
            {
                await ProcessTimerAsync(db, chat, pipelineEngine, timer, now, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Timer {TimerId} ({Name}) failed", timer.Id, timer.Name);
            }
        }
    }

    private async Task ProcessTimerAsync(
        IApplicationDbContext db,
        IChatProvider chat,
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

        bool fired = timer.PipelineId is Guid pipelineId
            ? await FirePipelineAsync(db, pipelineEngine, timer, pipelineId, channelCtx, ct)
            : await FireMessageAsync(chat, timer, channelCtx, ct);
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

    /// <summary>The classic timer: sends the next round-robin chat line. True when a line was sent.</summary>
    private async Task<bool> FireMessageAsync(
        IChatProvider chat,
        Timer timer,
        ChannelContext channelCtx,
        CancellationToken ct
    )
    {
        if (timer.Messages.Count == 0)
            return false;

        // Pick next message (round-robin)
        string messageTemplate = timer.Messages[timer.NextMessageIndex % timer.Messages.Count];

        // Resolve template variables (template resolution is keyed by the tenant Guid)
        string message = await _templateResolver.ResolveAsync(
            messageTemplate,
            new Dictionary<string, string>(),
            timer.BroadcasterId,
            ct
        );

        if (string.IsNullOrWhiteSpace(message))
            return false;

        // IChatProvider takes the tenant Guid and resolves it to the Twitch channel string id internally.
        await chat.SendMessageAsync(channelCtx.BroadcasterId, message, cancellationToken: ct);

        _logger.LogInformation(
            "Timer {Name} fired in channel {BroadcasterId}: \"{Message}\"",
            timer.Name,
            timer.BroadcasterId,
            message
        );
        return true;
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
        string? graphJson = await db
            .Pipelines.Where(p =>
                p.Id == pipelineId && p.BroadcasterId == timer.BroadcasterId && p.DeletedAt == null
            )
            .Select(p => p.GraphJsonCache)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(graphJson))
        {
            _logger.LogWarning(
                "Timer {Name} points at pipeline {PipelineId} with no executable graph — skipping this fire",
                timer.Name,
                pipelineId
            );
            return true;
        }

        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["timer.name"] = timer.Name,
        };
        if (timer.Messages.Count > 0)
            variables["timer.message"] = timer.Messages[
                timer.NextMessageIndex % timer.Messages.Count
            ];

        PipelineRequest request = new()
        {
            BroadcasterId = timer.BroadcasterId,
            PipelineJson = graphJson,
            // A timer has no triggering chatter — the channel itself is the actor.
            TriggeredByUserId = channelCtx.TwitchChannelId,
            TriggeredByDisplayName = channelCtx.ChannelName,
            MessageId = string.Empty,
            RawMessage = string.Empty,
            InitialVariables = variables,
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
