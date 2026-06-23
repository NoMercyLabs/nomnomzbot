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
/// Why the self-host bot chose the port it did (deployment-distribution §6 — smart port handling). On its very first
/// boot the bot prefers its configured port, replaces a stale duplicate of itself, or steps aside onto a free
/// ephemeral port; whatever it binds it then <b>locks</b>. Once locked, the bot keeps that exact port forever so the
/// OAuth redirect URLs registered against it stay valid — it reclaims a stale duplicate of itself but refuses to move
/// to a different port for an unrelated app (that would break every saved login), surfacing a <see cref="LockConflict"/>.
/// </summary>
public enum PortResolution
{
    /// <summary>The preferred (configured) port was free and is being used as-is (first boot — this port is now locked).</summary>
    PreferredFree,

    /// <summary>The preferred port was held by a stale/duplicate NomNomzBot process, which was killed; we then took it.</summary>
    ReplacedStaleInstance,

    /// <summary>The preferred port was held by another, unrelated application; a free ephemeral port was bound instead (first boot — now locked).</summary>
    EphemeralFallback,

    /// <summary>The previously locked port was available (free, or reclaimed from a stale duplicate of us) and is being used.</summary>
    HonoredLock,

    /// <summary>
    /// The locked port is held by another, unrelated application. The bot will NOT move to a different port — the
    /// OAuth redirect URLs are registered on the locked port — so boot is aborted with a clear, actionable message.
    /// </summary>
    LockConflict,
}

/// <summary>The outcome of <see cref="IListenPortResolver"/>: the port to bind and the reason, for a clear boot log line.</summary>
public readonly record struct ListenPortDecision(int Port, PortResolution Reason, int PreferredPort)
{
    /// <summary>True when the bot cannot bind its committed port and must abort boot rather than silently move.</summary>
    public bool IsConflict => Reason == PortResolution.LockConflict;

    /// <summary>A human-readable, log-ready sentence describing the decision.</summary>
    public string Describe() =>
        Reason switch
        {
            PortResolution.PreferredFree => $"Listening on {Port}.",
            PortResolution.HonoredLock =>
                $"Listening on locked port {Port} (kept stable so the registered OAuth redirect URLs keep working).",
            PortResolution.ReplacedStaleInstance =>
                $"Port {PreferredPort} was held by a stale NomNomzBot instance — replaced it and took {Port}.",
            PortResolution.EphemeralFallback =>
                $"Port {PreferredPort} is held by another application — listening on {Port} instead (the UI discovers the actual port over the LAN).",
            PortResolution.LockConflict =>
                $"Port {Port} is reserved for this bot — its OAuth redirect URLs are registered on it — but is held by "
                    + "another application. Free that port and start NomNomzBot again. The bot will not move to a "
                    + "different port because that would break every saved login.",
            _ => $"Listening on {Port}.",
        };
}

/// <summary>
/// Thrown when the self-host bot cannot bind its committed (locked) port because an unrelated application holds it.
/// Surfaced to the operator (a dialog on the windowless single binary) rather than silently moving to another port,
/// which would invalidate the OAuth redirect URLs registered against the locked port (deployment-distribution §6).
/// </summary>
public sealed class ListenPortLockedException : Exception
{
    public ListenPortLockedException(string message)
        : base(message) { }
}
