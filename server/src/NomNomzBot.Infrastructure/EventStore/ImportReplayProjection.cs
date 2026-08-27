// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// "Replay" (event-store §1.1). <b>2026-08-27 incident:</b> this projection used to re-publish every
/// imported event onto the LIVE <see cref="IEventBus"/> so old-bot side effects (pipeline triggers, TTS,
/// reward handling) would fire again. Because it is driven automatically, forever, for every channel by
/// <see cref="EventStoreProjectionDriver"/>, that made it a live-side-effect cannon: a single rebuild
/// re-fired real Spotify calls, TTS utterances, and Helix redemption updates across 17 broadcasters, on a
/// schedule nobody could see or stop (<c>PauseAsync</c> exists but <see cref="ProjectionRunner.RunOnceAsync"/>
/// never checks a checkpoint's <c>Status</c> before draining it).
/// <para>
/// Owner directive (2026-08-27): a replay must be completely silent — no chat, no Spotify, no outbound call
/// of any kind, ever, to any outside system. This projection is now a pure no-op: it advances its checkpoint
/// (so <c>ProjectionCheckpointDto</c> still reports "caught up") without deserializing or publishing
/// anything. If "re-run old-bot side effects for an imported channel" is ever wanted again, it must be a
/// separate, explicitly-invoked, non-automatic action — never something the background driver does to every
/// channel by default.
/// </para>
/// </summary>
public sealed class ImportReplayProjection : IProjection
{
    public string Name => "import-replay";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes { get; } = new HashSet<string>();

    public Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success());

    public Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success()); // no derived table of our own — nothing to clear
}
