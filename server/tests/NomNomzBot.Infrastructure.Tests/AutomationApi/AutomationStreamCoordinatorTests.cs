// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.AutomationApi.Stream;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.AutomationApi;

/// <summary>
/// Proves the §4.2 socket protocol end to end over a scripted connection: <c>hello</c> announces the
/// auth requirement on connect; an unauthenticated socket accepts ONLY <c>authenticate</c> (any other
/// op is answered <c>unauthenticated</c> and no session appears) and is CLOSED when the auth window
/// lapses; a valid in-band authenticate registers the session; <c>subscribe</c> requires the
/// <c>events</c> scope and stores the patterns (wildcards included); <c>invoke</c> routes through the
/// same command service the REST plane uses and mirrors its result as a correlated
/// <c>response</c> frame; and disconnecting unregisters the session.
/// </summary>
public sealed class AutomationStreamCoordinatorTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f301");
    private const string Secret = "nnzb_ak_valid";

    private sealed class FakeConnection : IAutomationStreamConnection
    {
        private readonly Channel<string?> _incoming =
            System.Threading.Channels.Channel.CreateUnbounded<string?>();

        // Fires once per SendAsync so waiters can react to the exact frame that unblocks them instead
        // of polling on a wall-clock interval — the arrival is observed by signal, not by elapsed time.
        private readonly Channel<string> _sentNotify =
            System.Threading.Channels.Channel.CreateUnbounded<string>();
        private readonly Lock _gate = new();
        private readonly List<string> _sent = [];

        public bool Closed { get; private set; }
        public string? CloseReason { get; private set; }

        // Fires once, the moment CloseAsync runs — lets a waiter await the close deterministically
        // instead of racing a fixed wall-clock deadline against the coordinator's own task completion.
        public TaskCompletionSource ClosedSignal { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Sent
        {
            get
            {
                lock (_gate)
                    return [.. _sent];
            }
        }

        public void Queue(string frame) => _incoming.Writer.TryWrite(frame);

        public void QueueClientClose() => _incoming.Writer.TryWrite(null);

        public Task SendAsync(string frameJson, CancellationToken ct)
        {
            lock (_gate)
                _sent.Add(frameJson);
            _sentNotify.Writer.TryWrite(frameJson);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveTextAsync(CancellationToken ct) =>
            await _incoming.Reader.ReadAsync(ct);

        public Task CloseAsync(string reason, CancellationToken ct)
        {
            Closed = true;
            CloseReason = reason;
            _incoming.Writer.TryComplete();
            ClosedSignal.TrySetResult();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Waits for <paramref name="probe"/> to become non-null, woken by every <see cref="SendAsync"/>
        /// rather than by sleeping and re-checking on a timer — deterministic under CPU contention because
        /// the wait ends the instant the awaited frame lands, not after some fixed poll interval elapses.
        /// The bound is a deadlock guard only: it exists to fail a genuinely-hung test, not to measure the
        /// property under test, so it is generous on purpose.
        /// </summary>
        public async Task<T> WaitForSentAsync<T>(Func<T?> probe, string what)
            where T : class
        {
            if (probe() is { } already)
                return already;

            using CancellationTokenSource deadlockGuard = new(TimeSpan.FromSeconds(30));
            try
            {
                await foreach (string _ in _sentNotify.Reader.ReadAllAsync(deadlockGuard.Token))
                {
                    if (probe() is { } hit)
                        return hit;
                }
            }
            catch (OperationCanceledException) when (deadlockGuard.IsCancellationRequested) { }

            throw new Xunit.Sdk.XunitException($"Timed out waiting for {what}.");
        }
    }

    private sealed class Harness
    {
        public required AutomationStreamCoordinator Coordinator { get; init; }
        public required FakeConnection Connection { get; init; }
        public required IAutomationSessionRegistry Sessions { get; init; }
        public required IAutomationCommandService Commands { get; init; }
        public required FakeTimeProvider Clock { get; init; }
    }

    private static Harness Build(IReadOnlyList<string>? tokenScopes = null)
    {
        IAutomationTokenAuthenticator authenticator =
            Substitute.For<IAutomationTokenAuthenticator>();
        AutomationPrincipal principal = new(
            Channel,
            Guid.NewGuid(),
            "deck-token",
            tokenScopes ?? ["invoke", "read", "events", "chat"],
            null
        );
        authenticator
            .AuthenticateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                ci.ArgAt<string>(0) == Secret
                    ? Result.Success(principal)
                    : Result.Failure<AutomationPrincipal>(
                        "Invalid automation token.",
                        "UNAUTHENTICATED"
                    )
            );

        IAutomationCommandService commands = Substitute.For<IAutomationCommandService>();
        commands
            .InvokePipelineAsync(
                Arg.Any<AutomationPrincipal>(),
                Arg.Any<AutomationInvokeRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(new AutomationInvokeResult(Guid.NewGuid(), Guid.NewGuid(), true))
            );

        ServiceCollection services = new();
        services.AddSingleton(authenticator);
        services.AddSingleton(commands);
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        AutomationSessionRegistry sessions = new();
        IDeploymentProfileService profile = Substitute.For<IDeploymentProfileService>();
        profile.Current.Returns(
            new DeploymentProfileSnapshot(
                Guid.NewGuid(),
                default,
                false,
                default,
                default,
                default,
                default,
                default,
                default,
                false,
                default
            )
        );
        FakeTimeProvider clock = new();

        return new()
        {
            Coordinator = new(
                scopeFactory,
                sessions,
                profile,
                clock,
                NullLogger<AutomationStreamCoordinator>.Instance
            ),
            Connection = new(),
            Sessions = sessions,
            Commands = commands,
            Clock = clock,
        };
    }

    private static JsonElement Frame(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>Delegates to the connection's own signal-driven wait — see <see cref="FakeConnection.WaitForSentAsync{T}"/>.</summary>
    private static Task<T> WaitFor<T>(Harness h, Func<T?> probe, string what)
        where T : class => h.Connection.WaitForSentAsync(probe, what);

    [Fact]
    public async Task Hello_then_in_band_authenticate_registers_a_session_and_subscribe_stores_patterns()
    {
        Harness h = Build();
        Task run = h.Coordinator.RunAsync(
            h.Connection,
            headerPrincipal: null,
            CancellationToken.None
        );

        // hello announces the auth requirement immediately.
        string hello = await WaitFor(h, () => h.Connection.Sent.FirstOrDefault(), "hello frame");
        JsonElement helloFrame = Frame(hello);
        helloFrame.GetProperty("op").GetString().Should().Be("hello");
        helloFrame.GetProperty("data").GetProperty("authRequired").GetBoolean().Should().BeTrue();

        h.Connection.Queue($$"""{ "op": "authenticate", "id": "1", "token": "{{Secret}}" }""");
        await WaitFor(h, () => h.Connection.Sent.Skip(1).FirstOrDefault(), "authenticate response");
        Frame(h.Connection.Sent[1]).GetProperty("status").GetString().Should().Be("ok");

        h.Connection.Queue(
            """{ "op": "subscribe", "id": "2", "events": ["Supporter.Received", "Custom.*"] }"""
        );
        await WaitFor(h, () => h.Connection.Sent.Skip(2).FirstOrDefault(), "subscribe response");
        Frame(h.Connection.Sent[2]).GetProperty("status").GetString().Should().Be("ok");

        // The registered session matches exact names and wildcards, and never what it didn't ask for.
        AutomationSession session = h.Sessions.SubscribersOf("Supporter.Received").Single();
        session.IsSubscribedTo("Custom.HeartRate").Should().BeTrue("Custom.* is a wildcard");
        session.IsSubscribedTo("Twitch.ChatMessage").Should().BeFalse();

        h.Connection.QueueClientClose();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        h.Sessions.SubscribersOf("Supporter.Received").Should().BeEmpty("disconnect unregisters");
    }

    [Fact]
    public async Task An_unauthenticated_socket_accepts_only_authenticate_and_closes_on_timeout()
    {
        Harness h = Build();
        Task run = h.Coordinator.RunAsync(
            h.Connection,
            headerPrincipal: null,
            CancellationToken.None
        );
        await WaitFor(h, () => h.Connection.Sent.FirstOrDefault(), "hello frame");

        // Any other op pre-auth: rejected, and NO session ever appears.
        h.Connection.Queue("""{ "op": "subscribe", "id": "1", "events": ["*"] }""");
        string rejected = await WaitFor(
            h,
            () => h.Connection.Sent.Skip(1).FirstOrDefault(),
            "pre-auth rejection"
        );
        Frame(rejected)
            .GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("unauthenticated");
        h.Sessions.SubscribersOf("Supporter.Received").Should().BeEmpty();

        // The advertised window lapses → the server closes the socket. Advancing the fake clock fires
        // the coordinator's timeout synchronously; wait on the connection's own close signal (set inside
        // CloseAsync) rather than racing a fixed wall-clock deadline against real thread-pool scheduling —
        // the 30s bound below is a deadlock guard only, never something a healthy run should approach.
        h.Clock.Advance(TimeSpan.FromSeconds(11));
        await h.Connection.ClosedSignal.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await run;
        h.Connection.Closed.Should().BeTrue();
        h.Connection.CloseReason.Should().Be("authentication timeout");
    }

    [Fact]
    public async Task Header_authed_sockets_skip_the_auth_phase_and_invoke_routes_to_the_command_service()
    {
        Harness h = Build();
        AutomationPrincipal headerPrincipal = new(
            Channel,
            Guid.NewGuid(),
            "native-deck",
            ["invoke"],
            null
        );
        Task run = h.Coordinator.RunAsync(h.Connection, headerPrincipal, CancellationToken.None);

        string hello = await WaitFor(h, () => h.Connection.Sent.FirstOrDefault(), "hello frame");
        Frame(hello)
            .GetProperty("data")
            .GetProperty("authRequired")
            .GetBoolean()
            .Should()
            .BeFalse("the handshake header already authenticated this socket");

        h.Connection.Queue(
            """{ "op": "invoke", "id": "7", "pipelineName": "shoutout", "args": ["@someone"] }"""
        );
        string response = await WaitFor(
            h,
            () => h.Connection.Sent.Skip(1).FirstOrDefault(),
            "invoke response"
        );
        JsonElement invokeResponse = Frame(response);
        invokeResponse.GetProperty("id").GetString().Should().Be("7");
        invokeResponse.GetProperty("status").GetString().Should().Be("ok");
        invokeResponse.GetProperty("data").GetProperty("accepted").GetBoolean().Should().BeTrue();

        await h
            .Commands.Received(1)
            .InvokePipelineAsync(
                Arg.Is<AutomationPrincipal>(p => p.TokenName == "native-deck"),
                Arg.Is<AutomationInvokeRequest>(r =>
                    r.PipelineName == "shoutout" && r.Args!.Single() == "@someone"
                ),
                Arg.Any<CancellationToken>()
            );

        h.Connection.QueueClientClose();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Subscribe_without_the_events_scope_is_scope_denied()
    {
        Harness h = Build();
        AutomationPrincipal headerPrincipal = new(Channel, Guid.NewGuid(), "t", ["invoke"], null);
        Task run = h.Coordinator.RunAsync(h.Connection, headerPrincipal, CancellationToken.None);
        await WaitFor(h, () => h.Connection.Sent.FirstOrDefault(), "hello frame");

        h.Connection.Queue("""{ "op": "subscribe", "id": "1", "events": ["*"] }""");
        string response = await WaitFor(
            h,
            () => h.Connection.Sent.Skip(1).FirstOrDefault(),
            "subscribe response"
        );
        Frame(response)
            .GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("scope_denied");

        h.Connection.QueueClientClose();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
