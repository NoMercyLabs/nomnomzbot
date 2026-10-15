// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// The shared body of <c>!closequeue</c> and <c>!openqueue</c>: a music moderator (a moderator badge or the
/// <c>music:queue:moderate</c> grant, <see cref="MusicModerationGate"/>) sets whether new song requests are
/// taken. The music keeps playing and the queued songs stay.
/// </summary>
public abstract class QueueRequestsBuiltin(
    ISongRequestIntakeService intake,
    IBuiltinResponseComposer composer,
    MusicModerationGate gate
) : IBuiltinCommand
{
    public abstract string BuiltinKey { get; }
    public int DefaultCooldownSeconds => 5;

    // Checked in ExecuteAsync through the gate, so a non-mod chatter who holds the grant passes.
    public int DefaultMinPermissionLevel => 0;

    protected abstract bool Open { get; }
    protected abstract string ChangedSlot { get; }
    protected abstract string ChangedFallback { get; }
    protected abstract string UnchangedSlot { get; }
    protected abstract string UnchangedFallback { get; }
    protected abstract string FailedSlot { get; }
    protected abstract string FailedFallback { get; }

    public Task<bool> IsAvailableToAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    ) => gate.IsAllowedAsync(context, ct);

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        if (!await gate.IsAllowedAsync(context, ct))
            return Result.Success(await composer.ComposePermissionDeniedAsync(context, ct));

        Result<bool> changed = await intake.SetRequestsOpenAsync(
            context.BroadcasterId.ToString(),
            Open,
            ct
        );

        (string slot, string fallback) = SlotFor(changed);
        string reply = await composer.ComposeAsync(
            context,
            BuiltinKey,
            slot,
            fallback,
            new Dictionary<string, string>(),
            ct
        );
        return Result.Success(reply);
    }

    private (string Slot, string Fallback) SlotFor(Result<bool> changed)
    {
        if (changed.IsFailure)
            return (FailedSlot, FailedFallback);

        return changed.Value ? (ChangedSlot, ChangedFallback) : (UnchangedSlot, UnchangedFallback);
    }
}

/// <summary><c>!closequeue</c> — closes the song queue to new requests.</summary>
public sealed class CloseQueueBuiltin(
    ISongRequestIntakeService intake,
    IBuiltinResponseComposer composer,
    MusicModerationGate gate
) : QueueRequestsBuiltin(intake, composer, gate)
{
    public override string BuiltinKey => BuiltinResponseSlots.CloseQueue.Key;
    protected override bool Open => false;
    protected override string ChangedSlot => BuiltinResponseSlots.CloseQueue.Closed;
    protected override string ChangedFallback => "The song queue is now closed.";
    protected override string UnchangedSlot => BuiltinResponseSlots.CloseQueue.AlreadyClosed;
    protected override string UnchangedFallback => "The song queue is already closed.";
    protected override string FailedSlot => BuiltinResponseSlots.CloseQueue.Failed;
    protected override string FailedFallback =>
        "Could not close the song queue. Try again in a moment.";
}

/// <summary><c>!openqueue</c> — opens the song queue to new requests again.</summary>
public sealed class OpenQueueBuiltin(
    ISongRequestIntakeService intake,
    IBuiltinResponseComposer composer,
    MusicModerationGate gate
) : QueueRequestsBuiltin(intake, composer, gate)
{
    public override string BuiltinKey => BuiltinResponseSlots.OpenQueue.Key;
    protected override bool Open => true;
    protected override string ChangedSlot => BuiltinResponseSlots.OpenQueue.Opened;
    protected override string ChangedFallback => "The song queue is now open.";
    protected override string UnchangedSlot => BuiltinResponseSlots.OpenQueue.AlreadyOpen;
    protected override string UnchangedFallback => "The song queue is already open.";
    protected override string FailedSlot => BuiltinResponseSlots.OpenQueue.Failed;
    protected override string FailedFallback =>
        "Could not open the song queue. Try again in a moment.";
}
