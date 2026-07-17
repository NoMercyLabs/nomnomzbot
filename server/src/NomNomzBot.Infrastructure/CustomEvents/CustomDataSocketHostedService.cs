// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Infrastructure.CustomEvents.Sockets;

namespace NomNomzBot.Infrastructure.CustomEvents;

/// <summary>
/// The <c>socket</c> ingress runner (custom-events.md D2, the <c>supporter-events.md</c> hosted-service pattern):
/// one long-lived <see cref="System.Net.WebSockets.ClientWebSocket"/> per enabled <c>socket</c>-kind
/// <see cref="CustomDataSource"/> (Pulsoid/HypeRate heart-rate and any hand-rolled WS source). A ~30 s reconcile
/// tick opens connections for newly-enabled sources, drops connections whose source was disabled/deleted, and
/// restarts one whose endpoint or sealed secret changed. Each connection resolves the source's decrypted auth,
/// applies it the way the preset dictates (Pulsoid/HypeRate: a <c>?…token=</c> query parameter appended to the
/// endpoint), and streams inbound text frames straight to the single <see cref="ICustomDataIngestService"/> path
/// (JSONPath extraction, event publish, cache, <c>LastReceivedAt</c> stamp). A dropped stream reconnects with
/// exponential backoff (5 s → 60 s); a per-connection fault never tears down the service or the other
/// connections. Runs on one instance per cluster: the <see cref="IRunOnceGuard"/> lease is held across ticks and
/// re-tried while another instance owns it.
/// <para>
/// SECURITY — sockets have no egress-allowlist in the custom-events model (unlike <c>poll</c>, which is
/// FQDN-constrained via <c>HttpEgressAllowlist</c>); the boundary here is <c>wss://</c>-only: a non-<c>wss</c>
/// endpoint is rejected without a connect, and each frame is capped at 64 KB in the transport (mirrors ingest).
/// </para>
/// </summary>
internal sealed class CustomDataSocketHostedService : BackgroundService, IAsyncDisposable
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BackoffFloor = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackoffCeiling = TimeSpan.FromSeconds(60);

    private const string SecretProvider = "customdata";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICustomDataSocketFrameSource _frames;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomDataSocketHostedService> _logger;

    private readonly Dictionary<Guid, Runner> _runners = new();
    private IAsyncDisposable? _lease;

    public CustomDataSocketHostedService(
        IServiceScopeFactory scopeFactory,
        ICustomDataSocketFrameSource frames,
        TimeProvider clock,
        ILogger<CustomDataSocketHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _frames = frames;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(ReconcileInterval, _clock);
        try
        {
            do
            {
                try
                {
                    await ReconcileOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Custom data socket reconcile failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown — runners stop below via their linked tokens.
        }
        finally
        {
            await StopAllRunnersAsync();
        }
    }

    // Internal (not private) so tests can drive a single deterministic reconcile —
    // InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam.
    internal async Task ReconcileOnceAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        // The lease is held across ticks: sockets are long-lived, so ownership must be too. While another
        // instance holds it, this one keeps no runners and just re-tries next tick.
        if (_lease is null)
        {
            IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
            _lease = await guard.TryAcquireAsync("customdata-socket", LeaseTtl, ct);
            if (_lease is null)
                return;
        }

        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ITokenProtector protector = scope.ServiceProvider.GetRequiredService<ITokenProtector>();

        List<CustomDataSource> enabled = await db
            .CustomDataSources.Where(s =>
                s.IsEnabled && s.SourceKind == "socket" && s.DeletedAt == null
            )
            .ToListAsync(ct);

        // Stop runners whose source disappeared, was disabled, or changed its endpoint/credential.
        Dictionary<Guid, CustomDataSource> byId = enabled.ToDictionary(s => s.Id);
        foreach ((Guid id, Runner runner) in _runners.ToList())
        {
            bool keep =
                byId.TryGetValue(id, out CustomDataSource? current)
                && string.Equals(
                    ConnectFingerprint(current),
                    runner.Fingerprint,
                    StringComparison.Ordinal
                );
            if (keep)
                continue;
            await StopRunnerAsync(id, runner);
        }

        // Start a runner for each enabled source with a usable wss endpoint + resolvable auth.
        foreach (CustomDataSource source in enabled)
        {
            if (_runners.ContainsKey(source.Id))
                continue;

            string? secret = source.AuthSecretCipher is null
                ? null
                : await protector.TryUnprotectAsync(
                    source.AuthSecretCipher,
                    new TokenProtectionContext(
                        source.BroadcasterId.ToString(),
                        SecretProvider,
                        source.Id.ToString()
                    ),
                    ct
                );

            if (source.AuthSecretCipher is not null && string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogWarning(
                    "Custom data socket '{Source}' on channel {Channel} has an unresolvable sealed secret — skipped.",
                    source.Name,
                    source.BroadcasterId
                );
                continue;
            }

            Uri? endpoint = BuildConnectUri(source, secret);
            if (endpoint is null)
            {
                _logger.LogWarning(
                    "Custom data socket '{Source}' on channel {Channel} has no usable wss:// endpoint — skipped.",
                    source.Name,
                    source.BroadcasterId
                );
                continue;
            }

            Guid broadcasterId = source.BroadcasterId;
            string sourceName = source.Name;
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Runner runner = new(
                ConnectFingerprint(source),
                cts,
                Task.Run(
                    () => RunConnectionAsync(broadcasterId, sourceName, endpoint, cts.Token),
                    CancellationToken.None
                )
            );
            _runners[source.Id] = runner;
            _logger.LogInformation(
                "Custom data socket runner started for '{Source}' on channel {Channel}.",
                sourceName,
                broadcasterId
            );
        }
    }

    /// <summary>
    /// A stable identity for what the runner connected with — the endpoint plus the sealed cipher. A change to
    /// either (the streamer re-pointed the source or re-keyed it) restarts the runner on the next reconcile.
    /// </summary>
    private static string ConnectFingerprint(CustomDataSource source) =>
        $"{source.EndpointUrl} {source.AuthSecretCipher}";

    /// <summary>
    /// Builds the connect URI, applying the decrypted secret the way the socket presets dictate — a query-param
    /// token appended to the endpoint (Pulsoid <c>?access_token=</c>, HypeRate <c>?token=</c>). Returns null when
    /// the source has no endpoint, the result is not an absolute URI, or the scheme is not <c>wss</c> (the
    /// security boundary — plaintext <c>ws://</c> and every non-socket scheme are rejected without a connect).
    /// </summary>
    private static Uri? BuildConnectUri(CustomDataSource source, string? secret)
    {
        if (string.IsNullOrWhiteSpace(source.EndpointUrl))
            return null;

        string url = string.IsNullOrEmpty(secret)
            ? source.EndpointUrl
            : source.EndpointUrl + Uri.EscapeDataString(secret);

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return null;
        if (!string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            return null;
        return uri;
    }

    /// <summary>One source's receive loop: stream → ingest, reconnecting with backoff on any drop.</summary>
    private async Task RunConnectionAsync(
        Guid broadcasterId,
        string sourceName,
        Uri endpoint,
        CancellationToken ct
    )
    {
        TimeSpan backoff = BackoffFloor;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (string frame in _frames.ConnectAndReceiveAsync(endpoint, ct))
                {
                    backoff = BackoffFloor; // a live frame proves the stream healthy — reset the backoff.
                    await IngestAsync(broadcasterId, sourceName, frame, ct);
                }
                // Peer closed cleanly — treat like a drop and reconnect after the floor delay.
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Custom data socket '{Source}' on channel {Channel} dropped — reconnecting in {Backoff}.",
                    sourceName,
                    broadcasterId,
                    backoff
                );
            }

            try
            {
                await Task.Delay(backoff, _clock, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, BackoffCeiling.Ticks));
        }
    }

    private async Task IngestAsync(
        Guid broadcasterId,
        string sourceName,
        string frame,
        CancellationToken ct
    )
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ICustomDataIngestService ingest =
                scope.ServiceProvider.GetRequiredService<ICustomDataIngestService>();
            Result ingested = await ingest.IngestAsync(broadcasterId, sourceName, frame, ct);
            if (ingested.IsFailure)
                _logger.LogWarning(
                    "Custom data socket ingest failed for '{Source}' on channel {Channel}: {Error}",
                    sourceName,
                    broadcasterId,
                    ingested.ErrorMessage
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Custom data socket ingest crashed for '{Source}' on channel {Channel}.",
                sourceName,
                broadcasterId
            );
        }
    }

    private async Task StopRunnerAsync(Guid id, Runner runner)
    {
        _runners.Remove(id);
        await runner.Cts.CancelAsync();
        try
        {
            await runner.Loop;
        }
        catch (OperationCanceledException)
        {
            // Expected — the loop ends by cancellation.
        }
        runner.Cts.Dispose();
    }

    private async Task StopAllRunnersAsync()
    {
        foreach ((Guid id, Runner runner) in _runners.ToList())
            await StopRunnerAsync(id, runner);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllRunnersAsync();
        if (_lease is not null)
            await _lease.DisposeAsync();
        Dispose();
    }

    /// <summary>A live per-source receive loop + the endpoint/credential identity it started with (restart signal).</summary>
    private sealed record Runner(string Fingerprint, CancellationTokenSource Cts, Task Loop);
}
