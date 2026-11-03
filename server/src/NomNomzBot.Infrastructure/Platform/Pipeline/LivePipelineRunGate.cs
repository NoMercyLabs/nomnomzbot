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

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Lets one live test run of a pipeline go at a time. A live run does real things, so a second start while one is
/// still running (a double click, a second browser tab) must be refused, not queued. Process-wide on purpose: the
/// service is scoped per request, so the gate cannot live on it.
/// </summary>
internal static class LivePipelineRunGate
{
    private static readonly ConcurrentDictionary<Guid, byte> Running = new();

    /// <summary>A lease for <paramref name="pipelineId"/> that frees the gate when disposed; null when a live run of it is already going.</summary>
    public static IDisposable? TryEnter(Guid pipelineId) =>
        Running.TryAdd(pipelineId, 0) ? new Lease(pipelineId) : null;

    private sealed class Lease(Guid pipelineId) : IDisposable
    {
        public void Dispose() => Running.TryRemove(pipelineId, out _);
    }
}
