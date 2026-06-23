// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Configuration;
using NomNomzBot.Domain.Enums.Deployment;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// Proves the boot wiring of smart port handling + the OAuth-stability port lock (deployment-distribution §6): the
/// preferred loopback port is parsed out of <c>Urls</c>, SaaS is left untouched, a self-host run rewrites <c>Urls</c>
/// and points <c>App:BaseUrl</c> at the bound port, the first boot locks the chosen port, a later boot reuses it
/// without re-writing, and an unresolvable lock conflict aborts boot. The OS-level decision itself is covered by
/// <see cref="ListenPortResolverTests"/>; here the resolver and the lock store are faked so the wiring is asserted
/// without real sockets or touching the real data dir.
/// </summary>
public sealed class ListenPortBootstrapTests
{
    [Theory]
    [InlineData("http://localhost:5080", true, 5080)]
    [InlineData("http://127.0.0.1:5080", true, 5080)]
    [InlineData("http://localhost:5080;https://localhost:5081", true, 5080)]
    [InlineData("https://localhost:5081;http://localhost:5080", true, 5080)] // first http loopback wins
    [InlineData("http://0.0.0.0:5080", false, 0)] // wildcard bind is not a loopback we shift
    [InlineData("http://bot.example.com:443", false, 0)] // real host — left to the operator
    [InlineData("https://localhost:5081", false, 0)] // https-only loopback isn't shifted
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void TryGetLoopbackPort_extracts_the_first_http_loopback_port(
        string? urls,
        bool expectedFound,
        int expectedPort
    )
    {
        bool found = ListenPortBootstrap.TryGetLoopbackPort(urls, out int port);

        found.Should().Be(expectedFound);
        port.Should().Be(expectedPort);
    }

    [Fact]
    public void Saas_binding_is_left_untouched_and_never_resolves_or_locks()
    {
        IConfiguration configuration = Config(("Urls", "http://localhost:5080"));
        FakeLockedPortStore store = new();
        FakeResolver resolver = new(Decide(5080, PortResolution.PreferredFree, 5080));

        int? resolved = ListenPortBootstrap.ResolveAndApply(
            configuration,
            DeploymentMode.Saas,
            NullLogger.Instance,
            store,
            resolver
        );

        resolved.Should().BeNull();
        configuration["Urls"]
            .Should()
            .Be("http://localhost:5080", "SaaS keeps its configured binding");
        resolver.Called.Should().BeFalse("SaaS must not run smart port resolution");
        store.WriteCalled.Should().BeFalse("SaaS must never write a self-host lock file");
    }

    [Fact]
    public void First_boot_with_no_urls_configured_defaults_to_5080_and_locks_it()
    {
        // appsettings (and its Urls) was not found from this launch directory. The bootstrap must NOT leave the
        // binding unset — that lets Kestrel fall back to its reserved port 5000 and crash (WSAEACCES) — but resolve
        // from the self-host default port instead.
        IConfiguration configuration = Config(); // no Urls at all
        FakeLockedPortStore store = new();
        FakeResolver resolver = new(Decide(5080, PortResolution.PreferredFree, 5080));

        int? resolved = ListenPortBootstrap.ResolveAndApply(
            configuration,
            DeploymentMode.SelfHostLite,
            NullLogger.Instance,
            store,
            resolver
        );

        resolved.Should().Be(5080);
        resolver.Called.Should().BeTrue();
        resolver
            .ReceivedPreferredPort.Should()
            .Be(5080, "with no Urls the bootstrap falls back to the self-host default port");
        configuration["Urls"].Should().Be("http://localhost:5080");
        configuration["App:BaseUrl"].Should().Be("http://localhost:5080");
        store.Written.Should().Be(5080);
    }

    [Fact]
    public void Self_host_without_a_loopback_url_leaves_the_binding_untouched()
    {
        IConfiguration configuration = Config(("Urls", "http://0.0.0.0:5080"));
        FakeLockedPortStore store = new();
        FakeResolver resolver = new(Decide(5080, PortResolution.PreferredFree, 5080));

        int? resolved = ListenPortBootstrap.ResolveAndApply(
            configuration,
            DeploymentMode.SelfHostFull,
            NullLogger.Instance,
            store,
            resolver
        );

        resolved.Should().BeNull();
        configuration["Urls"].Should().Be("http://0.0.0.0:5080");
        resolver.Called.Should().BeFalse();
        store.WriteCalled.Should().BeFalse();
    }

    [Fact]
    public void First_boot_locks_the_bound_port_and_points_base_url_at_it()
    {
        IConfiguration configuration = Config(("Urls", "http://localhost:5080"));
        FakeLockedPortStore store = new(); // nothing locked yet ⇒ first boot
        FakeResolver resolver = new(Decide(5080, PortResolution.PreferredFree, 5080));

        int? resolved = ListenPortBootstrap.ResolveAndApply(
            configuration,
            DeploymentMode.SelfHostLite,
            NullLogger.Instance,
            store,
            resolver
        );

        resolved.Should().Be(5080);
        resolver.ReceivedLockedPort.Should().BeNull("a first boot has no committed port to honor");
        configuration["Urls"].Should().Be("http://localhost:5080");
        configuration["App:BaseUrl"]
            .Should()
            .Be("http://localhost:5080", "OAuth callbacks must target the bound port");
        store.WriteCalled.Should().BeTrue("the first boot commits to the port it bound");
        store.Written.Should().Be(5080);
    }

    [Fact]
    public void First_boot_that_steps_aside_locks_the_ephemeral_port_it_actually_bound()
    {
        IConfiguration configuration = Config(("Urls", "http://localhost:5080"));
        FakeLockedPortStore store = new();
        FakeResolver resolver = new(Decide(51234, PortResolution.EphemeralFallback, 5080));

        int? resolved = ListenPortBootstrap.ResolveAndApply(
            configuration,
            DeploymentMode.SelfHostLite,
            NullLogger.Instance,
            store,
            resolver
        );

        resolved.Should().Be(51234);
        configuration["Urls"].Should().Be("http://localhost:51234");
        configuration["App:BaseUrl"].Should().Be("http://localhost:51234");
        store
            .Written.Should()
            .Be(51234, "the bot locks the port it actually bound, not the preferred one");
    }

    [Fact]
    public void Later_boot_reuses_the_locked_port_without_re_writing_it()
    {
        IConfiguration configuration = Config(("Urls", "http://localhost:5080"));
        FakeLockedPortStore store = new() { Locked = 51999 }; // a prior boot already committed
        FakeResolver resolver = new(Decide(51999, PortResolution.HonoredLock, 51999));

        int? resolved = ListenPortBootstrap.ResolveAndApply(
            configuration,
            DeploymentMode.SelfHostLite,
            NullLogger.Instance,
            store,
            resolver
        );

        resolved.Should().Be(51999);
        resolver
            .ReceivedLockedPort.Should()
            .Be(51999, "the committed port is passed to the resolver to honor");
        configuration["Urls"].Should().Be("http://localhost:51999");
        configuration["App:BaseUrl"].Should().Be("http://localhost:51999");
        store.WriteCalled.Should().BeFalse("an already-locked port must not be re-committed");
    }

    [Fact]
    public void Lock_conflict_aborts_boot_and_does_not_move_or_re_lock()
    {
        IConfiguration configuration = Config(("Urls", "http://localhost:5080"));
        FakeLockedPortStore store = new() { Locked = 51999 };
        FakeResolver resolver = new(Decide(51999, PortResolution.LockConflict, 51999));

        Action act = () =>
            ListenPortBootstrap.ResolveAndApply(
                configuration,
                DeploymentMode.SelfHostLite,
                NullLogger.Instance,
                store,
                resolver
            );

        act.Should().Throw<ListenPortLockedException>();
        configuration["Urls"]
            .Should()
            .Be(
                "http://localhost:5080",
                "a conflict must not rewrite the binding to a port we cannot use"
            );
        store.WriteCalled.Should().BeFalse();
    }

    [Fact]
    public void An_explicit_external_base_url_is_preserved()
    {
        // A self-host operator who fronts the bot with a domain/tunnel owns the public URL; the local port lock keeps
        // that tunnel's target stable, but App:BaseUrl must stay the external URL the OAuth providers are registered with.
        IConfiguration configuration = Config(
            ("Urls", "http://localhost:5080"),
            ("App:BaseUrl", "https://bot.example.com")
        );
        FakeLockedPortStore store = new();
        FakeResolver resolver = new(Decide(5080, PortResolution.PreferredFree, 5080));

        ListenPortBootstrap.ResolveAndApply(
            configuration,
            DeploymentMode.SelfHostFull,
            NullLogger.Instance,
            store,
            resolver
        );

        configuration["App:BaseUrl"]
            .Should()
            .Be(
                "https://bot.example.com",
                "an explicit external base URL must win over the loopback default"
            );
        configuration["Urls"].Should().Be("http://localhost:5080");
    }

    [Fact]
    public void A_loopback_base_url_is_rewritten_to_the_bound_port()
    {
        // A stale App:BaseUrl pointing at a different loopback port must follow the actual bound port, not desync from it.
        IConfiguration configuration = Config(
            ("Urls", "http://localhost:5080"),
            ("App:BaseUrl", "http://localhost:5080")
        );
        FakeLockedPortStore store = new() { Locked = 51999 };
        FakeResolver resolver = new(Decide(51999, PortResolution.HonoredLock, 51999));

        ListenPortBootstrap.ResolveAndApply(
            configuration,
            DeploymentMode.SelfHostLite,
            NullLogger.Instance,
            store,
            resolver
        );

        configuration["App:BaseUrl"].Should().Be("http://localhost:51999");
    }

    [Fact]
    public void Self_host_free_preferred_port_uses_the_real_resolver_and_store_seam()
    {
        // Integration-ish: the DEFAULT real resolver runs against a genuinely free OS port, with only the lock store
        // faked so the test never writes to the real per-user data dir. Proves the production wiring resolves + locks.
        int freePort = new SystemPortOperations().FindFreeEphemeralPort();
        IConfiguration configuration = Config(("Urls", $"http://localhost:{freePort}"));
        FakeLockedPortStore store = new();

        int? resolved = ListenPortBootstrap.ResolveAndApply(
            configuration,
            DeploymentMode.SelfHostLite,
            NullLogger.Instance,
            store
        );

        resolved.Should().Be(freePort);
        configuration["Urls"].Should().Be($"http://localhost:{freePort}");
        store.Written.Should().Be(freePort);
    }

    private static ListenPortDecision Decide(int port, PortResolution reason, int preferredPort) =>
        new(port, reason, preferredPort);

    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value))
            )
            .Build();

    private sealed class FakeLockedPortStore : ILockedPortStore
    {
        public int? Locked { get; set; }
        public int? Written { get; private set; }
        public bool WriteCalled { get; private set; }

        public int? Read() => Locked;

        public void Write(int port)
        {
            WriteCalled = true;
            Written = port;
        }
    }

    private sealed class FakeResolver : IListenPortResolver
    {
        private readonly ListenPortDecision _decision;

        public FakeResolver(ListenPortDecision decision) => _decision = decision;

        public bool Called { get; private set; }
        public int ReceivedPreferredPort { get; private set; }
        public int? ReceivedLockedPort { get; private set; }

        public ListenPortDecision Resolve(int preferredPort, int? lockedPort)
        {
            Called = true;
            ReceivedPreferredPort = preferredPort;
            ReceivedLockedPort = lockedPort;
            return _decision;
        }
    }
}
