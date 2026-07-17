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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Ipc;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NomNomzBot.Infrastructure.Services.Ipc;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Ipc;

/// <summary>
/// Behavior of the IPC dev-mode local socket listener (stream-admin.md §7), driven over the
/// in-memory transport seam so the protocol is proven OS-agnostically: auth-first with one refusal
/// then close (nothing else reachable), runtime revocation refusing every NEW connection, the SaaS
/// profile never binding, and the connection-cap / frame-cap / idle-timeout bounds enforced.
/// </summary>
public sealed class IpcDevModeListenerServiceTests : IAsyncDisposable
{
    private readonly AuthDbContext _db = AuthTestBuilder.NewContext();
    private readonly FakeTimeProvider _clock = new();
    private readonly FakeIpcListenerFactory _factory = new();
    private readonly ServiceProvider _provider;
    private readonly IpcDevModeService _registry;
    private IpcDevModeListenerService? _service;

    public IpcDevModeListenerServiceTests()
    {
        IDeploymentProfileService profile = Substitute.For<IDeploymentProfileService>();
        profile.Current.Returns(
            new DeploymentProfileSnapshot(
                Guid.NewGuid(),
                DeploymentMode.SelfHostLite,
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
        _registry = new IpcDevModeService(_db, profile, _clock);

        ServiceCollection services = new();
        services.AddScoped<IIpcDevModeService>(_ => new IpcDevModeService(_db, profile, _clock));
        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_service is not null)
        {
            await _service.StopAsync(CancellationToken.None);
            _service.Dispose();
        }
        await _provider.DisposeAsync();
    }

    private async Task<IpcDevModeListenerService> StartListenerAsync(
        DeploymentMode mode = DeploymentMode.SelfHostLite
    )
    {
        _service = new IpcDevModeListenerService(
            _factory,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new DeploymentContext(mode),
            _clock,
            NullLogger<IpcDevModeListenerService>.Instance
        );
        await _service.StartAsync(CancellationToken.None);
        return _service;
    }

    private async Task<string> MintKeyAsync()
    {
        Result<IpcDevModeKeyDto> created = await _registry.CreateKeyAsync(
            Guid.CreateVersion7(),
            new CreateIpcKeyRequest("listener test", ExpiresAt: null)
        );
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        return created.Value!.PlaintextKey!;
    }

    /// <summary>Connects and completes the auth handshake, asserting the ok frame.</summary>
    private async Task<InMemoryIpcConnection> ConnectAuthenticatedAsync(string key)
    {
        InMemoryIpcConnection connection = new();
        _factory.Listener.Enqueue(connection);
        connection.Send($"{{\"key\":\"{key}\"}}");
        JsonDocument ok = await connection.NextResponseAsync();
        ok.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        return connection;
    }

    [Fact]
    public async Task An_unauthenticated_frame_gets_one_refusal_and_the_connection_closes()
    {
        await MintKeyAsync();
        await StartListenerAsync();

        InMemoryIpcConnection connection = new();
        _factory.Listener.Enqueue(connection);

        // Not an auth frame at all — nothing but the handshake is reachable before auth.
        connection.Send("{\"type\":\"ping\"}");

        JsonDocument refusal = await connection.NextResponseAsync();
        refusal.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        refusal.RootElement.GetProperty("error").GetString().Should().Be("FORBIDDEN");

        await connection.WaitClosedAsync();
        connection.Send("{\"type\":\"ping\"}");
        (await connection.ResponsesEndedAsync())
            .Should()
            .BeTrue("after the refusal the server must process nothing further");
    }

    [Fact]
    public async Task A_wrong_key_is_refused_and_closed()
    {
        await MintKeyAsync();
        await StartListenerAsync();

        InMemoryIpcConnection connection = new();
        _factory.Listener.Enqueue(connection);
        connection.Send("{\"key\":\"nnzb_ipc_" + new string('0', 64) + "\"}");

        JsonDocument refusal = await connection.NextResponseAsync();
        refusal.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        refusal.RootElement.GetProperty("error").GetString().Should().Be("FORBIDDEN");
        await connection.WaitClosedAsync();
    }

    [Fact]
    public async Task A_valid_key_authenticates_and_requests_are_served()
    {
        string key = await MintKeyAsync();
        await StartListenerAsync();

        InMemoryIpcConnection connection = await ConnectAuthenticatedAsync(key);

        connection.Send("{\"type\":\"ping\"}");
        JsonDocument pong = await connection.NextResponseAsync();
        pong.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        pong.RootElement.GetProperty("type").GetString().Should().Be("pong");

        connection.Send("{\"type\":\"status\"}");
        JsonDocument status = await connection.NextResponseAsync();
        status.RootElement.GetProperty("type").GetString().Should().Be("status");
        status
            .RootElement.GetProperty("enabled")
            .GetBoolean()
            .Should()
            .BeTrue("the minted key keeps dev mode on");

        connection.Send("{\"type\":\"no-such-request\"}");
        JsonDocument unknown = await connection.NextResponseAsync();
        unknown.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        unknown.RootElement.GetProperty("error").GetString().Should().Be("UNKNOWN_REQUEST");

        connection.Send("{\"type\":\"ping\"}");
        JsonDocument stillServed = await connection.NextResponseAsync();
        stillServed
            .RootElement.GetProperty("type")
            .GetString()
            .Should()
            .Be("pong", "an unknown request is an error frame, not a hangup");
    }

    [Fact]
    public async Task A_key_revoked_at_runtime_refuses_every_new_connection()
    {
        string key = await MintKeyAsync();
        await StartListenerAsync();

        InMemoryIpcConnection established = await ConnectAuthenticatedAsync(key);

        Guid keyId = (await _registry.ListKeysAsync()).Value!.Single().Id;
        (await _registry.RevokeKeyAsync(keyId)).IsSuccess.Should().BeTrue();

        InMemoryIpcConnection late = new();
        _factory.Listener.Enqueue(late);
        late.Send($"{{\"key\":\"{key}\"}}");
        JsonDocument refusal = await late.NextResponseAsync();
        refusal.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        refusal
            .RootElement.GetProperty("error")
            .GetString()
            .Should()
            .Be("FORBIDDEN", "auth re-checks the registry per connection, so revocation bites NOW");
        await late.WaitClosedAsync();

        // The already-established session survives (revocation gates new connections, not live ones)
        // — but its status request now truthfully reports dev mode off.
        established.Send("{\"type\":\"status\"}");
        JsonDocument status = await established.NextResponseAsync();
        status.RootElement.GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task The_saas_profile_never_binds_the_listener()
    {
        await StartListenerAsync(DeploymentMode.Saas);

        _factory.BindCount.Should().Be(0, "the SaaS binary must never open the local socket");
    }

    [Fact]
    public async Task The_fifth_concurrent_connection_is_refused_politely()
    {
        string key = await MintKeyAsync();
        await StartListenerAsync();

        // Authenticate sequentially so the shared test DbContext is never hit concurrently.
        List<InMemoryIpcConnection> active = [];
        for (int i = 0; i < IpcDevModeListenerService.MaxConcurrentConnections; i++)
            active.Add(await ConnectAuthenticatedAsync(key));

        InMemoryIpcConnection fifth = new();
        _factory.Listener.Enqueue(fifth);
        JsonDocument busy = await fifth.NextResponseAsync();
        busy.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        busy.RootElement.GetProperty("error").GetString().Should().Be("BUSY");
        await fifth.WaitClosedAsync();

        // Freeing one slot readmits the next client.
        active[0].CloseClient();
        await active[0].WaitClosedAsync();
        InMemoryIpcConnection replacement = await ConnectAuthenticatedAsync(key);
        replacement.Should().NotBeNull();
    }

    [Fact]
    public async Task An_oversized_frame_is_refused_and_the_connection_closes()
    {
        string key = await MintKeyAsync();
        await StartListenerAsync();
        InMemoryIpcConnection connection = await ConnectAuthenticatedAsync(key);

        connection.SendOversized();

        JsonDocument refusal = await connection.NextResponseAsync();
        refusal.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        refusal.RootElement.GetProperty("error").GetString().Should().Be("FRAME_TOO_LARGE");
        await connection.WaitClosedAsync();
    }

    [Fact]
    public async Task An_idle_connection_is_closed_after_the_timeout()
    {
        string key = await MintKeyAsync();
        await StartListenerAsync();
        InMemoryIpcConnection connection = await ConnectAuthenticatedAsync(key);

        // The handler re-arms its idle timer per read; keep advancing until the armed timer fires.
        for (int attempt = 0; attempt < 100 && !connection.IsClosed; attempt++)
        {
            _clock.Advance(IpcDevModeListenerService.IdleTimeout + TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }

        connection.IsClosed.Should().BeTrue("a silent session must not hold its slot forever");
    }

    [Fact]
    public async Task Stop_closes_the_listener_and_drains_live_connections()
    {
        string key = await MintKeyAsync();
        IpcDevModeListenerService service = await StartListenerAsync();
        InMemoryIpcConnection connection = await ConnectAuthenticatedAsync(key);

        await service.StopAsync(CancellationToken.None);

        _factory.Listener.Disposed.Should().BeTrue("shutdown must unbind the local endpoint");
        await connection.WaitClosedAsync();
    }

    // ── In-memory transport fakes (the IIpcListenerFactory seam) ─────────────

    private sealed class FakeIpcListenerFactory : IIpcListenerFactory
    {
        public FakeIpcListener Listener { get; } = new();

        public int BindCount { get; private set; }

        public IIpcListener Bind()
        {
            BindCount++;
            return Listener;
        }
    }

    private sealed class FakeIpcListener : IIpcListener
    {
        private readonly Channel<IIpcConnection> _pending =
            Channel.CreateUnbounded<IIpcConnection>();

        public bool Disposed { get; private set; }

        public string EndpointDescription => "in-memory";

        public void Enqueue(IIpcConnection connection) => _pending.Writer.TryWrite(connection);

        public async Task<IIpcConnection?> AcceptAsync(CancellationToken ct)
        {
            try
            {
                return await _pending.Reader.ReadAsync(ct);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _pending.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>One fake duplex connection: the test plays the client, the service the server.</summary>
    private sealed class InMemoryIpcConnection : IIpcConnection
    {
        private readonly Channel<IpcReadResult> _toServer =
            Channel.CreateUnbounded<IpcReadResult>();
        private readonly Channel<string> _fromServer = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource _closed = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public bool IsClosed => _closed.Task.IsCompleted;

        public async Task<IpcReadResult> ReadFrameAsync(CancellationToken ct)
        {
            try
            {
                return await _toServer.Reader.ReadAsync(ct);
            }
            catch (ChannelClosedException)
            {
                return IpcReadResult.Closed;
            }
        }

        public Task WriteFrameAsync(string json, CancellationToken ct)
        {
            _fromServer.Writer.TryWrite(json);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _closed.TrySetResult();
            _toServer.Writer.TryComplete();
            _fromServer.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        // ── Client-side helpers ──────────────────────────────────────────────

        public void Send(string json) => _toServer.Writer.TryWrite(IpcReadResult.Of(json));

        /// <summary>Simulates the transport's 64&#160;KB frame-cap verdict.</summary>
        public void SendOversized() => _toServer.Writer.TryWrite(IpcReadResult.TooLarge);

        public void CloseClient() => _toServer.Writer.TryComplete();

        public async Task<JsonDocument> NextResponseAsync()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            string frame = await _fromServer.Reader.ReadAsync(timeout.Token);
            return JsonDocument.Parse(frame);
        }

        /// <summary>True when the server closed the connection without writing anything further.</summary>
        public async Task<bool> ResponsesEndedAsync()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            return !await _fromServer.Reader.WaitToReadAsync(timeout.Token);
        }

        public async Task WaitClosedAsync()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            await _closed.Task.WaitAsync(timeout.Token);
        }
    }
}
