// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;

/// <summary>
/// Suspends the run to wait for a named event (S-PIPE-TREE-d3b, built on the persistence core from
/// S-PIPE-TREE-d3a). The engine parks the run as a <c>PipelineRunState</c> row carrying
/// <c>event_name</c> and an absolute timeout deadline; the run resumes exactly once, either:
///
/// - MATCHED — a caller invokes <c>IPipelineEngine.ResumeSuspendedRunsForEventAsync</c> for this
///   channel with an event name that equals <c>event_name</c> (case-insensitive). Every key/value pair
///   the caller supplies as event data becomes readable by later steps as <c>{{event.&lt;key&gt;}}</c>,
///   plus <c>{{event.name}}</c>, <c>{{event.matched}}</c> ("true") and <c>{{event.timed_out}}</c>
///   ("false"). A DIFFERENT event name never resumes this run — the query that finds candidates filters
///   on the exact stored name, so an unrelated event firing for the same channel is a silent no-op here.
///
/// - TIMED OUT — DECISION (documented, not deferred): the wait does NOT fail the run and does NOT leave
///   it parked forever. Once <c>timeout_seconds</c> elapses, <c>IPipelineEngine.ResumeTimedOutWaitsAsync</c>
///   resumes the SAME run down the honest timeout path: execution continues at the very next step with
///   <c>{{event.matched}}</c>="false" and <c>{{event.timed_out}}</c>="true" (and <c>{{event.name}}</c>
///   still set to what it was waiting for) — so a pipeline author puts an <c>if event.timed_out</c>
///   check right after the wait to branch on it, exactly like any other action's output. Nothing about a
///   timeout is silent: <c>{{last.output}}</c> also carries the string <c>"timed_out"</c> for this step.
///
/// A suspended wait is cancelled (not deleted) if the channel goes offline while parked
/// (<c>PipelineEngine.CancelAllForChannelAsync</c>) — a run must never stay wired to a stream that isn't
/// live anymore.
/// </summary>
public sealed class WaitForEventAction : ICommandAction
{
    private readonly ITemplateResolver _resolver;

    /// <summary>Same shape as <see cref="WaitAction"/>'s per-step cap — a misconfigured/runaway wait
    /// must not park a run indefinitely; author a longer wait by raising this within the cap, there is
    /// no chaining trick needed since the run is parked (not burning runtime) while it waits.</summary>
    private const int MaxTimeoutSeconds = 24 * 60 * 60; // 24h
    private const int DefaultTimeoutSeconds = 300; // 5 minutes

    public string ActionType => "wait_for_event";

    public LocalizedText Category => new("pipeline.category.flow");

    public LocalizedText Description => new("pipeline.wait_for_event.description");
    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "event_name",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.wait_for_event.event_name.help")
            ),
            new(
                "timeout_seconds",
                PipelineActionFieldKind.Number,
                Templated: true,
                Description: new("pipeline.wait_for_event.timeout_seconds.help")
            ),
        ];

    public WaitForEventAction(ITemplateResolver resolver) => _resolver = resolver;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? rawEventName = action.GetString("event_name");
        string eventName = string.IsNullOrWhiteSpace(rawEventName)
            ? string.Empty
            : await _resolver.ResolveAsync(
                rawEventName,
                ctx.Variables,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
        if (string.IsNullOrWhiteSpace(eventName))
            return ActionResult.Failure("wait_for_event requires a non-empty event_name");

        string? rawTimeout = action.GetString("timeout_seconds");
        int timeoutSeconds;
        if (string.IsNullOrWhiteSpace(rawTimeout))
        {
            timeoutSeconds = action.GetInt("timeout_seconds", DefaultTimeoutSeconds);
        }
        else
        {
            string resolvedTimeout = await _resolver.ResolveAsync(
                rawTimeout,
                ctx.Variables,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
            timeoutSeconds = int.TryParse(resolvedTimeout, out int parsed)
                ? parsed
                : DefaultTimeoutSeconds;
        }

        if (timeoutSeconds <= 0)
            timeoutSeconds = DefaultTimeoutSeconds;
        if (timeoutSeconds > MaxTimeoutSeconds)
            timeoutSeconds = MaxTimeoutSeconds;

        return ActionResult.SuspendWaitingForEvent(eventName, timeoutSeconds);
    }
}
