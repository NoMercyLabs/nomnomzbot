// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// A test read-model: folds every <c>counter.incremented</c> event's <c>amount</c> into a running total keyed
/// by payload's <c>key</c>. <see cref="ApplyAsync"/> is idempotent because it sums each event exactly once and
/// the runner advances the checkpoint past each applied position; <see cref="ResetAsync"/> empties the model so
/// a rebuild starts from a known-empty state. The folded <see cref="State"/> is the observable consequence the
/// replay test asserts on.
/// </summary>
internal sealed class CounterProjection : IProjection
{
    public const string ProjectionName = "test.counter";

    public Dictionary<string, long> State { get; } = [];

    public string Name => ProjectionName;
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes { get; } =
        new HashSet<string> { "counter.incremented" };

    public Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        JObject payload = JObject.Parse(@event.PayloadJson);
        string key = payload.Value<string>("key") ?? "default";
        long amount = payload.Value<long?>("amount") ?? 0;

        State[key] = State.GetValueOrDefault(key) + amount;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        State.Clear();
        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// A test upcaster: the v1 payload of <c>counter.incremented</c> stored the increment under <c>value</c>; v2
/// renames it to <c>amount</c>. On read, v1 rows are rewritten so the projection only ever sees the v2 shape.
/// </summary>
internal sealed class CounterV1ToV2Upcaster : IEventUpcaster
{
    public string EventType => "counter.incremented";
    public int FromVersion => 1;

    public Result<string> Upcast(string payloadJson)
    {
        JObject payload = JObject.Parse(payloadJson);
        if (payload.TryGetValue("value", out JToken? value))
        {
            payload["amount"] = value;
            payload.Remove("value");
        }

        return Result.Success(payload.ToString(Newtonsoft.Json.Formatting.None));
    }
}

/// <summary>Records every committed journal row the decorator hands it, so a test can assert the hook fired.</summary>
internal sealed class RecordingPostCommitHook : IJournalPostCommitHook
{
    public List<EventRecord> Committed { get; } = [];

    public Task<Result> OnCommittedAsync(
        EventRecord committed,
        CancellationToken cancellationToken = default
    )
    {
        Committed.Add(committed);
        return Task.FromResult(Result.Success());
    }
}
