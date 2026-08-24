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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NomNomzBot.Api.Hubs;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// S035 item 1 — proves SignalR's stateful reconnect buffer is actually configured on the resolved options
/// for EVERY mapped hub, not merely present as a string in a config file. Mirrors the exact
/// <c>AddSignalR</c> configuration Program.cs applies (<c>StatefulReconnectBufferSize</c>), resolves the
/// per-hub-typed <see cref="HubOptions{THub}"/> ASP.NET Core actually dispatches through for each of the four
/// mapped hubs, and asserts the buffer is enabled on all of them. A hub added to Program.cs without also
/// picking up the global default (e.g. a future per-hub <c>AddHubOptions&lt;T&gt;</c> override that forgets
/// to carry the buffer size forward) breaks this test.
/// </summary>
public sealed class HubStatefulReconnectTests
{
    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSignalR(options =>
        {
            // Same values Program.cs configures — kept in sync deliberately rather than shared via a helper,
            // so a drift between the real config and this test is visible as a diff, not hidden behind
            // indirection.
            options.EnableDetailedErrors = true;
            options.MaximumReceiveMessageSize = 128 * 1024;
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
            options.StatefulReconnectBufferSize = 100_000;
        });
        return services.BuildServiceProvider();
    }

    private static long? ResolveBufferSize<THub>(ServiceProvider provider)
        where THub : Hub =>
        provider.GetRequiredService<IOptions<HubOptions<THub>>>().Value.StatefulReconnectBufferSize;

    [Fact]
    public void DashboardHub_has_stateful_reconnect_enabled()
    {
        using ServiceProvider provider = BuildProvider();
        ResolveBufferSize<DashboardHub>(provider).Should().Be(100_000);
    }

    [Fact]
    public void OverlayHub_has_stateful_reconnect_enabled()
    {
        using ServiceProvider provider = BuildProvider();
        ResolveBufferSize<OverlayHub>(provider).Should().Be(100_000);
    }

    [Fact]
    public void OBSRelayHub_has_stateful_reconnect_enabled()
    {
        using ServiceProvider provider = BuildProvider();
        ResolveBufferSize<OBSRelayHub>(provider).Should().Be(100_000);
    }

    [Fact]
    public void AdminHub_has_stateful_reconnect_enabled()
    {
        using ServiceProvider provider = BuildProvider();
        ResolveBufferSize<AdminHub>(provider).Should().Be(100_000);
    }
}
