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

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// The single EventType-agnostic post-commit seam. The <c>JournalingEventBusDecorator</c> — the only place
/// that already sees every event — invokes every registered hook AFTER a journal row commits and BEFORE it
/// delegates to the live bus handlers. Lets subsystems react to every journaled event (e.g. outbound webhook
/// fan-out) without writing one handler per concrete event type.
/// </summary>
public interface IJournalPostCommitHook
{
    /// <summary>
    /// Invoked once per successfully-committed journal row, after the <c>StreamPosition</c> is assigned and the
    /// txn committed. Read-only w.r.t. the journal (the row is immutable). Side effects belong to the hook's own
    /// subsystem. MUST be idempotent on <c>EventRecord.EventId</c> (the decorator may re-invoke after a transient
    /// downstream failure). Failures are isolated by the decorator — a faulting hook never rolls back the commit
    /// or blocks delegation to bus handlers.
    /// </summary>
    Task<Result> OnCommittedAsync(
        EventRecord committed,
        CancellationToken cancellationToken = default
    );
}
