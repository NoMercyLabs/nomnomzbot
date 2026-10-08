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
