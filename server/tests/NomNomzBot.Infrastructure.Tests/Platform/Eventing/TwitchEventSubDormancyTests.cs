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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        CapturingLogger<TwitchEventSubHostedService> logger
    )
    {
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IPlatformBotReadinessGate>(_ => new FakeReadinessGate(botConfigured))
            .BuildServiceProvider();

        return new TwitchEventSubHostedService(
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
                    return _messages.ToList();
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
