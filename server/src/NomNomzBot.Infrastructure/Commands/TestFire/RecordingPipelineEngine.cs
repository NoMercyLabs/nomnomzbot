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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Commands.TestFire;

/// <summary>Runs every pipeline on the real engine and remembers the steps that ran, so a test fire can list them.</summary>
internal sealed class RecordingPipelineEngine(IPipelineEngine inner) : IPipelineEngine
{
    private readonly List<StepExecutionLog> _steps = [];

    /// <summary>The steps of every run this engine started, in order.</summary>
    public IReadOnlyList<StepExecutionLog> Steps => _steps;

    public async Task<PipelineExecutionResult> ExecuteAsync(
        PipelineRequest request,
        CancellationToken ct = default
    )
    {
        PipelineExecutionResult result = await inner.ExecuteAsync(request, ct);
        _steps.AddRange(result.StepLogs);
        return result;
    }

    public Task CancelAllForChannelAsync(Guid broadcasterId) =>
        inner.CancelAllForChannelAsync(broadcasterId);

    public int GetActiveCountForChannel(Guid broadcasterId) =>
        inner.GetActiveCountForChannel(broadcasterId);

    public Task<Result<string?>> RunInlineSubPipelineAsync(
        PipelineExecutionContext callerCtx,
        Guid targetPipelineId,
        IReadOnlyList<string>? args,
        IReadOnlyDictionary<string, string>? namedArgs = null,
        CancellationToken ct = default
    ) => inner.RunInlineSubPipelineAsync(callerCtx, targetPipelineId, args, namedArgs, ct);

    public Task<PipelineExecutionResult> ResumeAsync(
        Guid runStateId,
        CancellationToken ct = default
    ) => inner.ResumeAsync(runStateId, ct);

    public Task<int> ResumeSuspendedRunsForEventAsync(
        Guid broadcasterId,
        string eventName,
        IReadOnlyDictionary<string, string> eventData,
        CancellationToken ct = default
    ) => inner.ResumeSuspendedRunsForEventAsync(broadcasterId, eventName, eventData, ct);

    public Task<int> ResumeTimedOutWaitsAsync(CancellationToken ct = default) =>
        inner.ResumeTimedOutWaitsAsync(ct);
}
