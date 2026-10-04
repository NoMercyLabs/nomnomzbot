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
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// <see cref="IScheduledPipelineDispatcher"/> that gives each channel its own queue. A run takes one of
/// <see cref="MaxConcurrentRuns"/> shared slots only after its channel's earlier run has ended, so a waiting run
/// holds no slot and one slow channel can never starve the others.
/// </summary>
public sealed class ScheduledPipelineDispatcher : IScheduledPipelineDispatcher, IDisposable
{
    /// <summary>How many runs may be inside the engine at the same moment, across all channels.</summary>
    internal const int MaxConcurrentRuns = 8;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledPipelineDispatcher> _logger;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentRuns, MaxConcurrentRuns);
    private readonly CancellationTokenSource _abort = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Task> _channelTails = [];

    // The engine is resolved lazily on a fresh scope at run time rather than constructor-injected: a `run_code`
    // action reaches IScriptRunner → IScheduledPipelineService (the schedule.pipeline capability), so a direct
    // engine dependency would close a static DI cycle (engine → run_code → runner → scheduler → engine).
    // Running on its own scope also isolates each deferred run's DbContext, matching the redemption path.
    public ScheduledPipelineDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledPipelineDispatcher> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Enqueue(ScheduledPipelineRun run)
    {
        Guid channel = run.Request.BroadcasterId;
        lock (_gate)
        {
            Task previous = _channelTails.GetValueOrDefault(channel, Task.CompletedTask);
            Task tail = RunAfterAsync(previous, run);
            _channelTails[channel] = tail;
            _ = tail.ContinueWith(
                done => ForgetTail(channel, done),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }
    }

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        Task[] pending;
        lock (_gate)
            pending = [.. _channelTails.Values];
        if (pending.Length == 0)
            return;

        try
        {
            await Task.WhenAll(pending).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Scheduled pipeline shutdown wait ended: cancelling the runs of {Channels} channel queue(s) still going",
                pending.Count(t => !t.IsCompleted)
            );
            await _abort.CancelAsync();
        }
    }

    public void Dispose()
    {
        _abort.Dispose();
        _slots.Dispose();
    }

    private void ForgetTail(Guid channel, Task done)
    {
        lock (_gate)
        {
            if (
                _channelTails.TryGetValue(channel, out Task? current)
                && ReferenceEquals(current, done)
            )
                _channelTails.Remove(channel);
        }
    }

    private async Task RunAfterAsync(Task previous, ScheduledPipelineRun run)
    {
        // Never run engine code on the sweep's thread; and the predecessor never faults (RunAsync catches all).
        await Task.Yield();
        await previous;

        try
        {
            await _slots.WaitAsync(_abort.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Deferred pipeline {PipelineName} (task {TaskId}) was dropped: the host is shutting down",
                run.PipelineName,
                run.TaskId
            );
            return;
        }

        try
        {
            await RunAsync(run);
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>
    /// Best-effort run through the pipeline engine. The engine returns a failed result (never throws) when the
    /// target pipeline was deleted or won't parse; a genuine fault is caught and logged so one bad task can never
    /// break the queue or leave a poisoned row behind (the row is already marked fired).
    /// </summary>
    private async Task RunAsync(ScheduledPipelineRun run)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IPipelineEngine engine = scope.ServiceProvider.GetRequiredService<IPipelineEngine>();
            PipelineExecutionResult result = await engine.ExecuteAsync(run.Request, _abort.Token);
            if (result.Outcome == PipelineOutcome.Failed)
                _logger.LogWarning(
                    "Deferred pipeline {PipelineId} ({PipelineName}) for channel {Channel} fired but failed: {Error}",
                    run.Request.PipelineId,
                    run.PipelineName,
                    run.Request.BroadcasterId,
                    result.ErrorMessage
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Deferred pipeline {PipelineId} for channel {Channel} threw during dispatch",
                run.Request.PipelineId,
                run.Request.BroadcasterId
            );
        }
    }
}
