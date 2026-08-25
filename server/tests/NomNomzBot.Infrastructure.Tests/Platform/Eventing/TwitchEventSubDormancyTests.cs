// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// Proves the EventSub host stays dormant until the platform bot is configured (the fresh-self-host fix): with no
/// bot token the transport is never started (no connect, no reconnect loop) and exactly one "waiting" line is
/// logged; with the bot configured the transport is started on boot. The transport is a recording substitute so
/// the assertion is on the actual consequence — whether a connection attempt was made — not on a log string.
/// </summary>
public sealed class TwitchEventSubDormancyTests
{
    private static TwitchEventSubHostedService Build(
        bool botConfigured,
        IEventSubTransport transport,
        CapturingLogger<TwitchEventSubHostedService> logger,
        ConcurrentDictionary<string, byte>? sharedLeases = null
    )
    {
        // A lease store SHARED between Build calls models the one database two colours contend over during
        // a switchover; the default gives each service its own, i.e. an uncontended single instance.
        ConcurrentDictionary<string, byte> leases = sharedLeases ?? new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IPlatformBotReadinessGate>(_ => new FakeReadinessGate(botConfigured))
            .AddScoped<IRunOnceGuard>(_ => new TwoInstanceLeaseStore(leases))
            .BuildServiceProvider();

        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            transport,
            new EventSubConditionBuilder(),
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            logger
        );
    }

    [Fact]
    public async Task FreshBot_DoesNotStartTheTransport_AndLogsOneWaitingLine()
    {
        IEventSubTransport transport = Substitute.For<IEventSubTransport>();
        CapturingLogger<TwitchEventSubHostedService> logger = new();
        TwitchEventSubHostedService service = Build(botConfigured: false, transport, logger);

        await service.StartAsync(CancellationToken.None);

        // The consequence that matters: no connection attempt was made against Twitch.
        await transport.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());

        // Exactly one informational "waiting" line — no reconnect/error loop spam.
        logger
            .Messages.Should()
            .ContainSingle(m => m.Contains("waiting for onboarding"))
            .And.NotContain(m => m.Contains("reconnect"));

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConfiguredBot_StartsTheTransport()
    {
        IEventSubTransport transport = Substitute.For<IEventSubTransport>();
        transport
            .StartAsync(Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new EventSubTransportHandle
                    {
                        Kind = EventSubTransportKind.WebSocket,
                        SessionId = "session-1",
                    }
                )
            );
        CapturingLogger<TwitchEventSubHostedService> logger = new();
        TwitchEventSubHostedService service = Build(botConfigured: true, transport, logger);

        await service.StartAsync(CancellationToken.None);

        await transport.Received(1).StartAsync(Arg.Any<CancellationToken>());
        logger.Messages.Should().NotContain(m => m.Contains("waiting for onboarding"));

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Only_one_instance_reads_chat_when_two_colours_overlap_during_a_deploy()
    {
        // The blue/green switchover deliberately runs both colours at once: the incoming one must pass
        // /health/ready before the outgoing one is drained. Harmless for HTTP (Caddy picks one), but EventSub
        // is not request-scoped — a second instance opens its own chat session and answers every command
        // twice. That is the "two bots" seen on stream, on every single deploy.
        ConcurrentDictionary<string, byte> sharedLeases = new();

        IEventSubTransport liveTransport = Substitute.For<IEventSubTransport>();
        liveTransport.StartAsync(Arg.Any<CancellationToken>()).Returns(Started());
        TwitchEventSubHostedService live = Build(
            botConfigured: true,
            liveTransport,
            new(),
            sharedLeases
        );

        IEventSubTransport incomingTransport = Substitute.For<IEventSubTransport>();
        incomingTransport.StartAsync(Arg.Any<CancellationToken>()).Returns(Started());
        CapturingLogger<TwitchEventSubHostedService> incomingLogger = new();
        TwitchEventSubHostedService incoming = Build(
            botConfigured: true,
            incomingTransport,
            incomingLogger,
            sharedLeases
        );

        await live.StartAsync(CancellationToken.None);
        await incoming.StartAsync(CancellationToken.None);

        // The consequence that matters: the second instance never opened a chat session.
        await liveTransport.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await incomingTransport.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        incomingLogger.Messages.Should().Contain(m => m.Contains("deploy overlap"));

        await incoming.StopAsync(CancellationToken.None);
        await live.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_outgoing_instance_hands_chat_over_when_it_stops()
    {
        ConcurrentDictionary<string, byte> sharedLeases = new();

        IEventSubTransport outgoingTransport = Substitute.For<IEventSubTransport>();
        outgoingTransport.StartAsync(Arg.Any<CancellationToken>()).Returns(Started());
        TwitchEventSubHostedService outgoing = Build(
            botConfigured: true,
            outgoingTransport,
            new(),
            sharedLeases
        );
        await outgoing.StartAsync(CancellationToken.None);

        // The outgoing colour drains and exits — chat must become available immediately, not after a TTL.
        await outgoing.StopAsync(CancellationToken.None);

        IEventSubTransport incomingTransport = Substitute.For<IEventSubTransport>();
        incomingTransport.StartAsync(Arg.Any<CancellationToken>()).Returns(Started());
        TwitchEventSubHostedService incoming = Build(
            botConfigured: true,
            incomingTransport,
            new(),
            sharedLeases
        );

        await incoming.StartAsync(CancellationToken.None);

        await incomingTransport.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await incoming.StopAsync(CancellationToken.None);
    }

    private static Result<EventSubTransportHandle> Started() =>
        Result.Success(
            new EventSubTransportHandle
            {
                Kind = EventSubTransportKind.WebSocket,
                SessionId = "session-1",
            }
        );

    /// <summary>One shared lease store standing in for the single database two instances contend over: the
    /// first caller for a name wins and holds it until disposed, every other caller is refused.</summary>
    private sealed class TwoInstanceLeaseStore(ConcurrentDictionary<string, byte> store)
        : IRunOnceGuard
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string resourceName,
            TimeSpan ttl,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<IAsyncDisposable?>(
                store.TryAdd(resourceName, 0) ? new Lease(store, resourceName) : null
            );

        private sealed class Lease(ConcurrentDictionary<string, byte> store, string name)
            : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                store.TryRemove(name, out _);
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>A readiness gate stuck at a fixed answer — drives the dormant vs active branch deterministically.</summary>
    private sealed class FakeReadinessGate(bool configured) : IPlatformBotReadinessGate
    {
        public Task<bool> IsPlatformBotConfiguredAsync(CancellationToken ct = default) =>
            Task.FromResult(configured);
    }

    /// <summary>Captures the rendered log messages so a test can assert exactly which lines were emitted.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                    return [.. _messages];
            }
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (_messages)
                _messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
