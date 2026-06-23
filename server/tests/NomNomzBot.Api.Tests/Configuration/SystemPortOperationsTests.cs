// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using NomNomzBot.Api.Configuration;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// Proves the real OS port seam follows through (deployment-distribution §6): bindability flips with an actual
/// loopback listener, an ephemeral port is genuinely free, and the self-process identity is reported. These use real
/// sockets on ephemeral ports only (no fixed well-known port), so they are safe and deterministic.
/// </summary>
public sealed class SystemPortOperationsTests
{
    private readonly SystemPortOperations _ops = new();

    [Fact]
    public void FindFreeEphemeralPort_returns_a_port_that_is_then_bindable()
    {
        int port = _ops.FindFreeEphemeralPort();

        port.Should().BeInRange(1, 65535);
        _ops.IsPortBindable(port).Should().BeTrue("the ephemeral port was released after probing");
    }

    [Fact]
    public void IsPortBindable_is_false_while_a_listener_holds_the_port_and_true_after_release()
    {
        int port = _ops.FindFreeEphemeralPort();

        TcpListener listener = new(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            _ops.IsPortBindable(port).Should().BeFalse("a real listener currently holds the port");
        }
        finally
        {
            listener.Stop();
        }

        _ops.IsPortBindable(port).Should().BeTrue("the listener was stopped, freeing the port");
    }

    [Fact]
    public void CurrentProcessName_matches_the_running_test_host_process()
    {
        using Process self = Process.GetCurrentProcess();

        _ops.CurrentProcessName.Should().Be(self.ProcessName);
    }

    [Fact]
    public void GetProcessName_of_the_current_process_is_resolvable()
    {
        using Process self = Process.GetCurrentProcess();

        _ops.GetProcessName(self.Id).Should().Be(self.ProcessName);
    }

    [Fact]
    public void GetProcessName_of_a_non_existent_pid_is_null_not_a_throw()
    {
        // A PID that cannot exist (negative) must surface as null so the resolver treats it as "another app".
        _ops.GetProcessName(-1).Should().BeNull();
    }
}
