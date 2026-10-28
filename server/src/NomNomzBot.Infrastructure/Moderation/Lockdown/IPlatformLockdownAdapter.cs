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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation.Lockdown;

/// <summary>What one control looks like on the platform right now.</summary>
/// <param name="PreviousValue">The value to put back, serialized by the adapter that will read it again.</param>
/// <param name="AlreadyLocked">True when the control is already at (or past) the locked value: nothing to engage, nothing to restore.</param>
public sealed record ControlReading(string PreviousValue, bool AlreadyLocked);

/// <summary>
/// The platform's own room controls behind a lockdown. One adapter per platform that has an API wired.
/// Every method only reads or tightens the room; none of them can act on a person.
/// </summary>
public interface IPlatformLockdownAdapter
{
    /// <summary>Lower-case platform key (<c>twitch</c>).</summary>
    string Platform { get; }

    /// <summary>Controls this adapter can really drive. A requested control outside this set is reported unavailable.</summary>
    IReadOnlySet<LockdownControl> Drives { get; }

    /// <summary>Read the current value of each control. A control whose read failed is absent from the result's success set.</summary>
    Task<IReadOnlyDictionary<LockdownControl, Result<ControlReading>>> ReadAsync(
        Guid broadcasterId,
        IReadOnlyCollection<LockdownControl> controls,
        CancellationToken ct
    );

    /// <summary>Tighten one control to its locked value.</summary>
    Task<Result> LockAsync(Guid broadcasterId, LockdownControl control, CancellationToken ct);

    /// <summary>Put one control back to the value it had (<see cref="EngagedControl.PreviousValue"/>).</summary>
    Task<Result> RestoreAsync(Guid broadcasterId, EngagedControl engaged, CancellationToken ct);
}
