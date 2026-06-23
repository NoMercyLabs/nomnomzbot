// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// The OS seams the <see cref="ListenPortResolver"/> needs, abstracted so the decision logic is unit-testable
/// without touching real sockets, the OS TCP table, or live processes. The production implementation is
/// <see cref="SystemPortOperations"/>; tests inject a fake.
/// </summary>
public interface IPortOperations
{
    /// <summary>True if a loopback <c>TcpListener</c> can be opened on <paramref name="port"/> right now (the port is free).</summary>
    bool IsPortBindable(int port);

    /// <summary>Bind a loopback listener on port 0 and return the ephemeral port the OS assigned, then release it.</summary>
    int FindFreeEphemeralPort();

    /// <summary>
    /// The PID of the process listening on <paramref name="port"/> over loopback, or <c>null</c> when it cannot be
    /// determined (unsupported OS, no permission, or the row vanished). Uncertainty MUST surface as <c>null</c> — the
    /// resolver treats an unknown owner as "another app" and never kills on a guess.
    /// </summary>
    int? GetListeningProcessId(int port);

    /// <summary>
    /// The process name (no extension) of <paramref name="pid"/>, or <c>null</c> if the process is gone / inaccessible.
    /// </summary>
    string? GetProcessName(int pid);

    /// <summary>The current process's name (no extension) — what a stale duplicate of ourselves would report.</summary>
    string CurrentProcessName { get; }

    /// <summary>
    /// Kill <paramref name="pid"/> and wait until it has exited (bounded). Returns true if the process is gone
    /// afterwards. Only ever called by the resolver once the owner has been positively confirmed to be our own exe.
    /// </summary>
    bool KillProcessAndWait(int pid, TimeSpan timeout);
}
