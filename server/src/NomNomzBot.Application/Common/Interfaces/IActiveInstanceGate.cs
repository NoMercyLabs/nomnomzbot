// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Interfaces;

/// <summary>
/// Which of two overlapping instances is the ACTIVE bot. A blue/green deploy runs both colours at once: the
/// incoming one waits as a STANDBY until the outgoing one hands chat ingest over. Work that must happen on
/// exactly one instance (chat ingest, the music hand-over poller, startup subscription sync) asks this gate
/// instead of assuming it is alone.
/// </summary>
public interface IActiveInstanceGate
{
    /// <summary>
    /// False while this instance is a standby waiting for another one to hand over, and false again once
    /// this instance has handed over during shutdown. True otherwise, including a single-instance host.
    /// </summary>
    bool IsActiveInstance { get; }

    /// <summary>Completes once this instance has become the active one (immediately when it already is).</summary>
    Task WaitUntilActiveAsync(CancellationToken ct);

    /// <summary>
    /// True when another instance is standing by to take over from this one — a blue/green handover, not a
    /// real outage. False for a plain restart, a crash-restart or a manual stop with no successor.
    /// </summary>
    Task<bool> HasWaitingSuccessorAsync(CancellationToken ct);
}
