// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace NomNomzBot.Api.Configuration;

/// <summary>Resolves the actual port the self-host bot should listen on (deployment-distribution §6).</summary>
public interface IListenPortResolver
{
    /// <summary>
    /// Decide the port to bind. <paramref name="lockedPort"/> is the port this install previously committed to (or
    /// <c>null</c> on the first boot). See <see cref="ListenPortResolver"/> for the rules — in short: with no lock, the
    /// preferred port is taken (replacing a stale copy of us, else stepping aside onto an ephemeral port); with a lock,
    /// that exact port is reclaimed (even from a stale copy of us) but never abandoned for an unrelated app, because the
    /// OAuth redirect URLs are registered on it.
    /// </summary>
    ListenPortDecision Resolve(int preferredPort, int? lockedPort);
}

/// <summary>
/// Smart self-host port handling so a port conflict never crashes the bot, while the bot's port stays stable for OAuth
/// (deployment-distribution §6). On its <b>first</b> boot the bot resolves a listen port and <b>locks</b> it:
/// <list type="number">
///   <item>Prefer the configured port. If it is <b>free</b> → use it.</item>
///   <item>If it is held by <b>another NomNomzBot instance</b> (same exe name) → that is a stale/duplicate of us, so
///   kill it (one canonical bot) and take the preferred port.</item>
///   <item>If it is held by <b>another, unrelated application</b> → bind a free <b>ephemeral</b> port instead. The
///   exact number doesn't matter yet because the UI discovers the bot — and its actual port — over the LAN via mDNS.</item>
/// </list>
/// Whatever it binds becomes the <b>locked</b> port (persisted by the bootstrap). On every later boot the bot is given
/// that locked port and must keep it: it still reclaims the port from a stale duplicate of itself, but if an
/// <b>unrelated</b> application holds it the bot does <b>not</b> step aside — moving would change its URL and break the
/// OAuth redirect URLs already registered against the locked port — so it returns <see cref="PortResolution.LockConflict"/>
/// for the bootstrap to abort boot with a clear message.
/// <para>
/// Safety: a process is only killed when its name positively matches our own exe. If the owning PID or its name cannot
/// be resolved (unsupported OS, no permission, race), the owner is treated as "another app" and the bot never kills on
/// uncertainty. All OS interaction goes through the injected <see cref="IPortOperations"/> seam so the decision logic is
/// unit-tested without real sockets or processes.
/// </para>
/// </summary>
public sealed class ListenPortResolver : IListenPortResolver
{
    // Bounded wait for a confirmed-stale sibling to exit after we kill it, before we re-take its port.
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

    private readonly IPortOperations _ops;
    private readonly ILogger<ListenPortResolver> _logger;

    public ListenPortResolver(IPortOperations ops, ILogger<ListenPortResolver> logger)
    {
        _ops = ops;
        _logger = logger;
    }

    public ListenPortDecision Resolve(int preferredPort, int? lockedPort)
    {
        bool locked = lockedPort is not null;
        // Once locked we insist on that exact port; otherwise we aim for the configured preferred one.
        int target = lockedPort ?? preferredPort;

        // 1. Target is free → take it.
        if (_ops.IsPortBindable(target))
            return new ListenPortDecision(
                target,
                locked ? PortResolution.HonoredLock : PortResolution.PreferredFree,
                target
            );

        // 2. Target is held. Find out by whom. Unknown owner ⇒ treat as another app (never kill on a guess).
        int? ownerPid = _ops.GetListeningProcessId(target);
        if (ownerPid is { } pid && IsOurOwnProcess(pid))
        {
            _logger.LogWarning(
                "Port {Port} is held by a stale NomNomzBot instance (pid {Pid}); replacing it so this stays the one "
                    + "canonical bot.",
                target,
                pid
            );

            if (_ops.KillProcessAndWait(pid, KillWait) && _ops.IsPortBindable(target))
                return new ListenPortDecision(target, PortResolution.ReplacedStaleInstance, target);

            // The kill didn't free the port in time (it lingered, or a third party grabbed it in the gap). When locked
            // we must not abandon the port (OAuth is registered on it) — abort with a conflict; otherwise step aside.
            if (locked)
            {
                _logger.LogError(
                    "Could not reclaim locked port {Port} from a stale NomNomzBot instance in time; aborting rather "
                        + "than moving to a different port (the OAuth redirect URLs are registered on this one).",
                    target
                );
                return new ListenPortDecision(target, PortResolution.LockConflict, target);
            }

            _logger.LogWarning(
                "Replaced the stale instance on port {Port}, but the port did not free up in time; using an ephemeral "
                    + "port instead.",
                target
            );
            return EphemeralFallback(preferredPort);
        }

        // 3. Held by an unrelated app (or an owner we couldn't identify).
        if (locked)
        {
            // Committed to this port for OAuth — do NOT silently move. Surface a conflict for the bootstrap to abort.
            if (ownerPid is null)
                _logger.LogError(
                    "Locked port {Port} is in use and its owner could not be identified — refusing to move to a "
                        + "different port (the OAuth redirect URLs are registered on this one).",
                    target
                );
            else
                _logger.LogError(
                    "Locked port {Port} is held by another application (pid {Pid}) — refusing to move to a different "
                        + "port (the OAuth redirect URLs are registered on this one).",
                    target,
                    ownerPid
                );
            return new ListenPortDecision(target, PortResolution.LockConflict, target);
        }

        if (ownerPid is null)
            _logger.LogInformation(
                "Port {Port} is in use and its owner could not be identified — treating it as another application and "
                    + "using an ephemeral port (no process is killed on uncertainty).",
                target
            );
        else
            _logger.LogInformation(
                "Port {Port} is held by another application (pid {Pid}) — using an ephemeral port instead.",
                target,
                ownerPid
            );

        return EphemeralFallback(preferredPort);
    }

    private ListenPortDecision EphemeralFallback(int preferredPort)
    {
        int ephemeral = _ops.FindFreeEphemeralPort();
        return new ListenPortDecision(ephemeral, PortResolution.EphemeralFallback, preferredPort);
    }

    // A stale duplicate of us reports the same process name. Resolve the owner's name and compare to ours; any
    // uncertainty (process gone, no name) returns false so we never kill something that isn't provably our own exe.
    private bool IsOurOwnProcess(int pid)
    {
        string? ownerName = _ops.GetProcessName(pid);
        if (string.IsNullOrEmpty(ownerName))
            return false;

        return string.Equals(
            ownerName,
            _ops.CurrentProcessName,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
