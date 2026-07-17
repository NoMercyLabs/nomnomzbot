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
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Platform.Deployment;

namespace NomNomzBot.Infrastructure.Services.Ipc;

/// <summary>
/// The IPC dev-mode local socket listener (stream-admin.md §7): a process-local dev hook-in over a
/// Unix domain socket (POSIX) / named pipe (Windows) — never a TCP port, never remote. Registered
/// only on self-host profiles (DI-gated like the mDNS advertiser) and double-guarded here so the
/// SaaS binary can never bind it. The wire is newline-delimited JSON: the first frame must be
/// <c>{"key":"nnzb_ipc_…"}</c>, verified per connection through a fresh scoped
/// <see cref="IIpcDevModeService.AuthenticateConnectionAsync"/> (so a key revoked at runtime
/// refuses every NEW connection); one refusal frame, then close. Authenticated connections get the
/// minimal request surface the registry seam backs: <c>ping</c> and <c>status</c>
/// (<see cref="IIpcDevModeService.IsEnabledAsync"/>). Connections are capped at
/// <see cref="MaxConcurrentConnections"/>, frames at <see cref="StreamIpcConnection.MaxFrameBytes"/>,
/// and idle sessions at <see cref="IdleTimeout"/> — all clock work rides <see cref="TimeProvider"/>.
/// </summary>
public sealed class IpcDevModeListenerService : IHostedService, IDisposable
{
    /// <summary>Dev tooling is one or two local processes; anything beyond this is refused politely.</summary>
    internal const int MaxConcurrentConnections = 4;

    /// <summary>A connection that sends nothing for this long is closed.</summary>
    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly IIpcListenerFactory _listenerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DeploymentContext _deployment;
    private readonly TimeProvider _clock;
    private readonly ILogger<IpcDevModeListenerService> _logger;

    private readonly ConcurrentDictionary<Task, byte> _connectionTasks = new();
    private CancellationTokenSource? _stopCts;
    private IIpcListener? _listener;
    private Task? _acceptLoop;
    private int _activeConnections;

    public IpcDevModeListenerService(
        IIpcListenerFactory listenerFactory,
        IServiceScopeFactory scopeFactory,
        DeploymentContext deployment,
        TimeProvider clock,
        ILogger<IpcDevModeListenerService> logger
    )
    {
        _listenerFactory = listenerFactory;
        _scopeFactory = scopeFactory;
        _deployment = deployment;
        _clock = clock;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Defense in depth: DI never registers this on SaaS, but even if it were added, refuse to bind.
        if (_deployment.Mode == DeploymentMode.Saas)
        {
            _logger.LogDebug("IPC dev-mode listener not started: the SaaS profile never binds it.");
            return Task.CompletedTask;
        }

        try
        {
            _listener = _listenerFactory.Bind();
        }
        catch (Exception ex)
        {
            // A dev convenience must never take the host down — log and run without it.
            _logger.LogError(
                ex,
                "IPC dev-mode listener failed to bind its local socket; dev hook-in is unavailable this run."
            );
            return Task.CompletedTask;
        }

        _stopCts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_stopCts.Token), CancellationToken.None);
        _logger.LogInformation(
            "IPC dev-mode listener bound to local endpoint {Endpoint}.",
            _listener.EndpointDescription
        );
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopCts is null)
            return;

        await _stopCts.CancelAsync();
        if (_listener is not null)
            await _listener.DisposeAsync(); // Unblocks the accept; deletes the socket file.

        List<Task> pending = [.. _connectionTasks.Keys];
        if (_acceptLoop is not null)
            pending.Add(_acceptLoop);
        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("IPC dev-mode connections did not drain within the shutdown grace.");
        }
    }

    public void Dispose() => _stopCts?.Dispose();

    private async Task AcceptLoopAsync(CancellationToken stopToken)
    {
        while (!stopToken.IsCancellationRequested)
        {
            IIpcConnection? connection;
            try
            {
                connection = await _listener!.AcceptAsync(stopToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (connection is null)
                return; // Listener closed.

            if (Interlocked.Increment(ref _activeConnections) > MaxConcurrentConnections)
            {
                Interlocked.Decrement(ref _activeConnections);
                Track(() => RefuseBusyAsync(connection, stopToken));
                continue;
            }

            Track(() => HandleConnectionAsync(connection, stopToken));
        }
    }

    /// <summary>Runs one connection's lifecycle on its own tracked task so shutdown can drain them.</summary>
    private void Track(Func<Task> work)
    {
        Task task = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "IPC dev-mode connection ended with an error.");
            }
        });
        _connectionTasks.TryAdd(task, 0);
        _ = task.ContinueWith(
            t => _connectionTasks.TryRemove(t, out byte _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static async Task RefuseBusyAsync(IIpcConnection connection, CancellationToken ct)
    {
        await using (connection)
        {
            await TryWriteAsync(
                connection,
                new
                {
                    ok = false,
                    error = "BUSY",
                    message = "Too many concurrent IPC connections.",
                },
                ct
            );
        }
    }

    private async Task HandleConnectionAsync(IIpcConnection connection, CancellationToken stopToken)
    {
        await using (connection)
        {
            try
            {
                if (!await AuthenticateAsync(connection, stopToken))
                    return;

                await ServeAuthenticatedAsync(connection, stopToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeConnections);
            }
        }
    }

    /// <summary>
    /// The auth handshake: exactly one <c>{"key":…}</c> frame, checked through a fresh scope so
    /// revocations and dev-mode-off are honored at connection time. One refusal, then close.
    /// </summary>
    private async Task<bool> AuthenticateAsync(
        IIpcConnection connection,
        CancellationToken stopToken
    )
    {
        IpcReadResult first = await ReadWithIdleTimeoutAsync(connection, stopToken);
        if (first.FrameTooLarge)
        {
            await RefuseAsync(connection, "FRAME_TOO_LARGE", "The frame exceeds 64 KB.", stopToken);
            return false;
        }
        if (first.IsClosed)
            return false;

        string? presentedKey = ExtractKey(first.Frame!);
        if (presentedKey is null)
        {
            await RefuseAsync(
                connection,
                "FORBIDDEN",
                "The first frame must be {\"key\":\"…\"}.",
                stopToken
            );
            return false;
        }

        Result auth;
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            IIpcDevModeService devMode =
                scope.ServiceProvider.GetRequiredService<IIpcDevModeService>();
            auth = await devMode.AuthenticateConnectionAsync(presentedKey, stopToken);
        }

        if (auth.IsFailure)
        {
            await RefuseAsync(
                connection,
                auth.ErrorCode ?? "FORBIDDEN",
                auth.ErrorMessage ?? "The presented IPC key is not valid.",
                stopToken
            );
            return false;
        }

        await TryWriteAsync(connection, new { ok = true }, stopToken);
        return true;
    }

    private async Task ServeAuthenticatedAsync(
        IIpcConnection connection,
        CancellationToken stopToken
    )
    {
        while (!stopToken.IsCancellationRequested)
        {
            IpcReadResult read = await ReadWithIdleTimeoutAsync(connection, stopToken);
            if (read.FrameTooLarge)
            {
                await RefuseAsync(
                    connection,
                    "FRAME_TOO_LARGE",
                    "The frame exceeds 64 KB.",
                    stopToken
                );
                return;
            }
            if (read.IsClosed)
                return;

            await RespondAsync(connection, read.Frame!, stopToken);
        }
    }

    private async Task RespondAsync(IIpcConnection connection, string frame, CancellationToken ct)
    {
        string? requestType;
        try
        {
            using JsonDocument document = JsonDocument.Parse(frame);
            requestType =
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out JsonElement typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()
                    : null;
        }
        catch (JsonException)
        {
            await TryWriteAsync(
                connection,
                new
                {
                    ok = false,
                    error = "MALFORMED_FRAME",
                    message = "Frames are one JSON object per line.",
                },
                ct
            );
            return;
        }

        switch (requestType)
        {
            case "ping":
                await TryWriteAsync(connection, new { ok = true, type = "pong" }, ct);
                return;

            case "status":
            {
                Result<bool> enabled;
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    IIpcDevModeService devMode =
                        scope.ServiceProvider.GetRequiredService<IIpcDevModeService>();
                    enabled = await devMode.IsEnabledAsync(ct);
                }
                await TryWriteAsync(
                    connection,
                    new
                    {
                        ok = true,
                        type = "status",
                        enabled = enabled.IsSuccess && enabled.Value,
                    },
                    ct
                );
                return;
            }

            default:
                await TryWriteAsync(
                    connection,
                    new
                    {
                        ok = false,
                        error = "UNKNOWN_REQUEST",
                        message = "Supported request types: ping, status.",
                    },
                    ct
                );
                return;
        }
    }

    /// <summary>One framed read bounded by the idle timeout; expiry reads as a closed peer.</summary>
    private async Task<IpcReadResult> ReadWithIdleTimeoutAsync(
        IIpcConnection connection,
        CancellationToken stopToken
    )
    {
        using CancellationTokenSource idleCts = new(IdleTimeout, _clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            stopToken,
            idleCts.Token
        );
        try
        {
            return await connection.ReadFrameAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            return IpcReadResult.Closed;
        }
    }

    private static string? ExtractKey(string frame)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(frame);
            return
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("key", out JsonElement keyElement)
                && keyElement.ValueKind == JsonValueKind.String
                ? keyElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task RefuseAsync(
        IIpcConnection connection,
        string error,
        string message,
        CancellationToken ct
    ) =>
        TryWriteAsync(
            connection,
            new
            {
                ok = false,
                error,
                message,
            },
            ct
        );

    /// <summary>Best-effort response write — a peer that vanished mid-write just closes the session.</summary>
    private static async Task TryWriteAsync(
        IIpcConnection connection,
        object payload,
        CancellationToken ct
    )
    {
        try
        {
            await connection.WriteFrameAsync(JsonSerializer.Serialize(payload, WireJson), ct);
        }
        catch (IOException)
        {
            // Peer gone.
        }
        catch (ObjectDisposedException)
        {
            // Connection torn down under us during shutdown.
        }
    }
}
