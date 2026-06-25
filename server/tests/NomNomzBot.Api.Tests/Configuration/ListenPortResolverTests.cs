// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Configuration;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// Proves the smart self-host port-resolution rule (deployment-distribution §6) and its kill safety, with every OS
/// seam faked so the logic is exercised without real sockets or live processes. The fake records whether a kill was
/// requested, so the "replace a stale duplicate of ourselves" path is verified to actually fire the kill while the
/// test kills nothing.
/// </summary>
public sealed class ListenPortResolverTests
{
    private const int PreferredPort = 5080;
    private const int EphemeralPort = 51234;
    private const string OurName = "nomnomz";

    [Fact]
    public void Preferred_port_free_is_used_as_is()
    {
        FakePortOperations ops = new() { BindablePorts = { PreferredPort, EphemeralPort } };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort: null);

        decision.Port.Should().Be(PreferredPort);
        decision.Reason.Should().Be(PortResolution.PreferredFree);
        ops.KilledPids.Should().BeEmpty();
    }

    [Fact]
    public void Preferred_port_held_by_our_own_stale_instance_kills_it_and_retakes_the_port()
    {
        // The preferred port is held by a process whose name matches OURS — a stale duplicate. The resolver must kill
        // it (recorded by the fake) and, once it frees, take the preferred port back.
        const int stalePid = 4242;
        FakePortOperations ops = new()
        {
            BindablePorts = { EphemeralPort }, // preferred is initially held
            ListenerPidByPort = { [PreferredPort] = stalePid },
            ProcessNameByPid = { [stalePid] = OurName },
            CurrentName = OurName,
            FreePortsAfterKill = { stalePid }, // killing it frees the preferred port
        };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort: null);

        decision.Port.Should().Be(PreferredPort);
        decision.Reason.Should().Be(PortResolution.ReplacedStaleInstance);
        ops.KilledPids.Should().ContainSingle().Which.Should().Be(stalePid);
    }

    [Fact]
    public void Preferred_port_held_by_another_app_falls_back_to_a_free_ephemeral_port_without_killing()
    {
        const int otherPid = 9999;
        FakePortOperations ops = new()
        {
            BindablePorts = { EphemeralPort },
            ListenerPidByPort = { [PreferredPort] = otherPid },
            ProcessNameByPid = { [otherPid] = "some-other-app" },
            CurrentName = OurName,
        };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort: null);

        decision.Port.Should().Be(EphemeralPort);
        decision.Port.Should().NotBe(PreferredPort);
        decision.Reason.Should().Be(PortResolution.EphemeralFallback);
        ops.KilledPids.Should().BeEmpty("an unrelated app must never be killed");
    }

    [Fact]
    public void Unknown_owner_is_treated_as_another_app_and_never_killed()
    {
        // The port is held but its owner PID cannot be resolved (e.g. non-Windows, or no permission). The resolver
        // must NOT kill on uncertainty — it steps aside onto a free port.
        FakePortOperations ops = new()
        {
            BindablePorts = { EphemeralPort },
            // No ListenerPidByPort entry for PreferredPort ⇒ GetListeningProcessId returns null.
            CurrentName = OurName,
        };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort: null);

        decision.Port.Should().Be(EphemeralPort);
        decision.Reason.Should().Be(PortResolution.EphemeralFallback);
        ops.KilledPids.Should().BeEmpty();
    }

    [Fact]
    public void Owner_pid_resolves_but_name_is_unavailable_is_not_killed()
    {
        // The PID is known but its name can't be read (process raced away / inaccessible) — provably-our-own is the
        // only kill trigger, so an unnameable owner is treated as another app.
        const int pid = 7000;
        FakePortOperations ops = new()
        {
            BindablePorts = { EphemeralPort },
            ListenerPidByPort = { [PreferredPort] = pid },
            // No ProcessNameByPid entry ⇒ GetProcessName returns null.
            CurrentName = OurName,
        };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort: null);

        decision.Reason.Should().Be(PortResolution.EphemeralFallback);
        ops.KilledPids.Should().BeEmpty();
    }

    [Fact]
    public void Stale_instance_killed_but_port_does_not_free_falls_back_to_ephemeral()
    {
        // We confirm + kill our stale sibling, but the port lingers (TIME_WAIT / a third party grabbed it). Rather
        // than crash-loop on the preferred port, the resolver steps aside onto a free ephemeral port.
        const int stalePid = 4243;
        FakePortOperations ops = new()
        {
            BindablePorts = { EphemeralPort }, // preferred never becomes bindable, even after the kill
            ListenerPidByPort = { [PreferredPort] = stalePid },
            ProcessNameByPid = { [stalePid] = OurName },
            CurrentName = OurName,
            // FreePortsAfterKill intentionally does NOT include stalePid ⇒ preferred stays unbindable.
        };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort: null);

        decision.Port.Should().Be(EphemeralPort);
        decision.Reason.Should().Be(PortResolution.EphemeralFallback);
        ops.KilledPids.Should().ContainSingle().Which.Should().Be(stalePid);
    }

    [Fact]
    public void Name_match_is_case_insensitive()
    {
        const int stalePid = 4244;
        FakePortOperations ops = new()
        {
            BindablePorts = { EphemeralPort },
            ListenerPidByPort = { [PreferredPort] = stalePid },
            ProcessNameByPid = { [stalePid] = "NomNomz" }, // different casing
            CurrentName = "nomnomz",
            FreePortsAfterKill = { stalePid },
        };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort: null);

        decision.Reason.Should().Be(PortResolution.ReplacedStaleInstance);
        ops.KilledPids.Should().ContainSingle().Which.Should().Be(stalePid);
    }

    // ── Locked-port mode (every later boot — the port is committed for OAuth and must stay stable) ──────────────

    [Fact]
    public void Locked_port_free_is_honored_as_is()
    {
        const int lockedPort = 51999;
        FakePortOperations ops = new() { BindablePorts = { lockedPort, EphemeralPort } };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort);

        decision
            .Port.Should()
            .Be(lockedPort, "the committed port must be reused, not the configured preferred one");
        decision.Reason.Should().Be(PortResolution.HonoredLock);
        ops.KilledPids.Should().BeEmpty();
    }

    [Fact]
    public void Locked_port_held_by_our_own_stale_instance_is_reclaimed()
    {
        // A stale duplicate of us squats on the locked port (the common "restarted / double-launched" case). We still
        // reclaim our own port — that is the whole point of the lock — by killing the confirmed sibling.
        const int lockedPort = 51999;
        const int stalePid = 4242;
        FakePortOperations ops = new()
        {
            BindablePorts = { EphemeralPort },
            ListenerPidByPort = { [lockedPort] = stalePid },
            ProcessNameByPid = { [stalePid] = OurName },
            CurrentName = OurName,
            FreePortsAfterKill = { stalePid },
        };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort);

        decision.Port.Should().Be(lockedPort);
        decision.Reason.Should().Be(PortResolution.ReplacedStaleInstance);
        ops.KilledPids.Should().ContainSingle().Which.Should().Be(stalePid);
    }

    [Fact]
    public void Locked_port_held_by_another_app_aborts_rather_than_moving()
    {
        // The committed port is taken by an unrelated app. Moving to a different port would break every OAuth redirect
        // URL registered against the locked port, so the resolver must signal a conflict (abort) — NOT step aside —
        // and must never kill the unrelated app.
        const int lockedPort = 51999;
        const int otherPid = 9999;
        FakePortOperations ops = new()
        {
            BindablePorts = { EphemeralPort },
            ListenerPidByPort = { [lockedPort] = otherPid },
            ProcessNameByPid = { [otherPid] = "some-other-app" },
            CurrentName = OurName,
        };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort);

        decision.Reason.Should().Be(PortResolution.LockConflict);
        decision.IsConflict.Should().BeTrue();
        decision
            .Port.Should()
            .Be(lockedPort, "the conflict reports the locked port, not an ephemeral one");
        decision.Port.Should().NotBe(EphemeralPort);
        ops.KilledPids.Should()
            .BeEmpty("an unrelated app holding the locked port must never be killed");
    }

    [Fact]
    public void Locked_port_unknown_owner_aborts_without_killing()
    {
        // The locked port is held but its owner can't be identified. Uncertainty must not move us off the committed
        // port (that breaks OAuth) and must not kill anything — it aborts with a conflict.
        const int lockedPort = 51999;
        FakePortOperations ops = new()
        {
            BindablePorts = { EphemeralPort },
            // No ListenerPidByPort entry ⇒ unknown owner.
            CurrentName = OurName,
        };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort);

        decision.Reason.Should().Be(PortResolution.LockConflict);
        ops.KilledPids.Should().BeEmpty();
    }

    [Fact]
    public void Locked_port_stale_sibling_killed_but_not_freed_aborts_instead_of_moving()
    {
        // Our own stale sibling held the locked port; we killed it but the port did not free in time. Unlocked we would
        // step aside onto an ephemeral port — but locked we must NOT move (OAuth is registered on this port), so it
        // aborts with a conflict.
        const int lockedPort = 51999;
        const int stalePid = 4243;
        FakePortOperations ops = new()
        {
            BindablePorts = { EphemeralPort }, // locked port never becomes bindable, even after the kill
            ListenerPidByPort = { [lockedPort] = stalePid },
            ProcessNameByPid = { [stalePid] = OurName },
            CurrentName = OurName,
            // FreePortsAfterKill intentionally omits stalePid ⇒ the locked port stays unbindable.
        };
        ListenPortResolver resolver = Build(ops);

        ListenPortDecision decision = resolver.Resolve(PreferredPort, lockedPort);

        decision.Reason.Should().Be(PortResolution.LockConflict);
        decision
            .Port.Should()
            .NotBe(EphemeralPort, "a locked install must never silently move to an ephemeral port");
        ops.KilledPids.Should().ContainSingle().Which.Should().Be(stalePid);
    }

    private static ListenPortResolver Build(IPortOperations ops) =>
        new(ops, NullLogger<ListenPortResolver>.Instance);

    /// <summary>
    /// A fully in-memory <see cref="IPortOperations"/>: bindability and PID/name lookups are driven by dictionaries,
    /// kills are recorded (not performed), and a kill can be configured to free a port so the retake path is exercised.
    /// </summary>
    private sealed class FakePortOperations : IPortOperations
    {
        public HashSet<int> BindablePorts { get; } = [];
        public Dictionary<int, int> ListenerPidByPort { get; } = [];
        public Dictionary<int, string> ProcessNameByPid { get; } = [];
        public HashSet<int> FreePortsAfterKill { get; } = [];
        public List<int> KilledPids { get; } = [];
        public string CurrentName { get; set; } = "nomnomz";

        public bool IsPortBindable(int port) => BindablePorts.Contains(port);

        public int FindFreeEphemeralPort() => EphemeralPort;

        public int? GetListeningProcessId(int port) =>
            ListenerPidByPort.TryGetValue(port, out int pid) ? pid : null;

        public string? GetProcessName(int pid) =>
            ProcessNameByPid.TryGetValue(pid, out string? name) ? name : null;

        public string CurrentProcessName => CurrentName;

        public bool KillProcessAndWait(int pid, TimeSpan timeout)
        {
            KilledPids.Add(pid);
            if (!FreePortsAfterKill.Contains(pid))
                return false;

            // Killing a confirmed-stale sibling frees the port(s) it held.
            foreach (KeyValuePair<int, int> entry in ListenerPidByPort)
                if (entry.Value == pid)
                    BindablePorts.Add(entry.Key);
            return true;
        }
    }
}
