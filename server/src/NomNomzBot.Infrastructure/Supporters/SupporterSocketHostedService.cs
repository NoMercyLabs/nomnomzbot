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
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Domain.Supporters.Entities;

namespace NomNomzBot.Infrastructure.Supporters;

/// <summary>
/// The <c>socket</c>/<c>ws</c> ingress runner (supporter-events.md §0 D3): one long-lived stream per enabled
/// live-socket <see cref="SupporterConnection"/> whose provider has a registered
/// <see cref="Sockets.ISupporterSocketProfile"/> (the provider dialect: endpoint, keepalive, which frames are
/// events). A ~30 s reconcile tick starts runners for newly-enabled connections, stops disabled/deleted ones,
/// and restarts a runner whose sealed secret changed. Frames translate to payloads that feed the single ingest
/// path — dedup on the provider transaction id absorbs a reconnect's replays. Runs on one instance per cluster:
/// the <see cref="IRunOnceGuard"/> lease is held across ticks and re-tried while another instance owns it.
/// A dropped stream reconnects with exponential backoff (5 s → 60 s) and marks the connection <c>error</c>
/// while down.
/// </summary>
internal sealed class SupporterSocketHostedService : BackgroundService, IAsyncDisposable
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BackoffFloor = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackoffCeiling = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Sockets.ISocketFrameSource _frames;
    private readonly IReadOnlyDictionary<string, Sockets.ISupporterSocketProfile> _profiles;
    private readonly TimeProvider _clock;
    private readonly ILogger<SupporterSocketHostedService> _logger;

    private readonly Dictionary<Guid, Runner> _runners = new();
    private IAsyncDisposable? _lease;

    public SupporterSocketHostedService(
        IServiceScopeFactory scopeFactory,
        Sockets.ISocketFrameSource frames,
        IEnumerable<Sockets.ISupporterSocketProfile> profiles,
        TimeProvider clock,
        ILogger<SupporterSocketHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _frames = frames;
        _profiles = profiles.ToDictionary(p => p.SourceKey, StringComparer.OrdinalIgnoreCase);
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
                    _logger.LogError(ex, "Supporter socket reconcile failed");
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
            _lease = await guard.TryAcquireAsync("supporters-socket", LeaseTtl, ct);
            if (_lease is null)
                return;
        }

        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<SupporterConnection> enabled = await db
            .SupporterConnections.Where(c =>
                c.IsEnabled
                && (c.ConnectionMode == "socket" || c.ConnectionMode == "ws")
                && c.DeletedAt == null
            )
            .ToListAsync(ct);

        // Stop runners whose connection disappeared, was disabled, or changed its credential.
        Dictionary<Guid, SupporterConnection> byId = enabled.ToDictionary(c => c.Id);
        foreach ((Guid id, Runner runner) in _runners.ToList())
        {
            bool keep =
                byId.TryGetValue(id, out SupporterConnection? current)
                && string.Equals(
                    CredentialFingerprint(current),
                    runner.CredentialFingerprint,
                    StringComparison.Ordinal
                );
            if (keep)
                continue;
            await StopRunnerAsync(id, runner);
        }

        // Start runners for enabled connections that have a profile + a resolvable credential.
        foreach (SupporterConnection connection in enabled)
        {
            if (_runners.ContainsKey(connection.Id))
                continue;
            if (
                !_profiles.TryGetValue(
                    connection.SourceKey,
                    out Sockets.ISupporterSocketProfile? profile
                )
            )
                continue; // provider has no live-socket dialect registered.

            string? fingerprint = CredentialFingerprint(connection);
            if (fingerprint is null)
            {
                connection.Status = "error";
                _logger.LogWarning(
                    "Supporter socket for {Source} on {Channel} has no usable key — set the connection secret or link the OAuth connection.",
                    connection.SourceKey,
                    connection.BroadcasterId
                );
                continue;
            }

            // The credential resolves PER CONNECT ATTEMPT (a vaulted OAuth access token rotates on refresh,
            // so a reconnect must never reuse the token captured at start). Validate it once now so a broken
            // credential errors the connection immediately instead of silently backoff-looping.
            Guid connectionId = connection.Id;
            Guid broadcasterId = connection.BroadcasterId;
            string sourceKey = connection.SourceKey;
            string? cipher = connection.AuthSecretCipher;
            Guid? oauthConnectionId = connection.IntegrationConnectionId;
            Func<CancellationToken, Task<string?>> resolveSecret = token =>
                ResolveSecretAsync(broadcasterId, sourceKey, cipher, oauthConnectionId, token);

            string? secret = await resolveSecret(ct);
            if (string.IsNullOrWhiteSpace(secret))
            {
                connection.Status = "error";
                _logger.LogWarning(
                    "Supporter socket for {Source} on {Channel} could not resolve its credential.",
                    connection.SourceKey,
                    connection.BroadcasterId
                );
                continue;
            }

            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Runner runner = new(
                fingerprint,
                cts,
                Task.Run(
                    () =>
                        RunConnectionAsync(
                            connectionId,
                            broadcasterId,
                            sourceKey,
                            resolveSecret,
                            profile,
                            cts.Token
                        ),
                    CancellationToken.None
                )
            );
            _runners[connection.Id] = runner;
            _logger.LogInformation(
                "Supporter socket runner started for {Source} on {Channel}.",
                connection.SourceKey,
                connection.BroadcasterId
            );
        }

        await db.SaveChangesAsync(ct); // persists any error-status flips
    }

    /// <summary>
    /// A stable identity for the connection's credential — the sealed cipher itself, or the linked OAuth
    /// connection (whose rotating token must NOT restart the runner on every refresh). Null = no credential.
    /// </summary>
    private static string? CredentialFingerprint(SupporterConnection connection) =>
        connection.AuthSecretCipher
        ?? (connection.IntegrationConnectionId is Guid oauthId ? $"oauth:{oauthId}" : null);

    /// <summary>
    /// The live credential for one connect attempt: the unsealed connection secret, or — for an
    /// OAuth-linked source (TreatStream) — the CURRENT vaulted access token, resolved fresh in its own
    /// scope so a rotated token is picked up on reconnect.
    /// </summary>
    private async Task<string?> ResolveSecretAsync(
        Guid broadcasterId,
        string sourceKey,
        string? cipher,
        Guid? oauthConnectionId,
        CancellationToken ct
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        if (cipher is not null)
        {
            ITokenProtector protector = scope.ServiceProvider.GetRequiredService<ITokenProtector>();
            return await protector.TryUnprotectAsync(
                cipher,
                SupporterConnectionService.SecretContext(broadcasterId, sourceKey),
                ct
            );
        }

        if (oauthConnectionId is Guid oauthId)
        {
            IIntegrationTokenVault vault =
                scope.ServiceProvider.GetRequiredService<IIntegrationTokenVault>();
            Result<Application.Identity.Dtos.DecryptedTokenDto> token =
                await vault.GetAccessTokenAsync(oauthId, ct);
            return token.IsSuccess ? token.Value.Value : null;
        }

        return null;
    }

    /// <summary>One connection's receive loop: stream → translate → ingest, reconnecting with backoff.</summary>
    private async Task RunConnectionAsync(
        Guid connectionId,
        Guid broadcasterId,
        string sourceKey,
        Func<CancellationToken, Task<string?>> resolveSecret,
        Sockets.ISupporterSocketProfile profile,
        CancellationToken ct
    )
    {
        TimeSpan backoff = BackoffFloor;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Resolved per attempt: an OAuth access token may have rotated since the last connect.
                string? secret = await resolveSecret(ct);
                if (string.IsNullOrWhiteSpace(secret))
                    throw new InvalidOperationException(
                        "The connection's credential no longer resolves."
                    );

                await foreach (string frame in _frames.ConnectAndReceiveAsync(profile, secret, ct))
                {
                    backoff = BackoffFloor; // a live frame proves the stream healthy — reset the backoff.
                    foreach (string payload in profile.TranslateFrame(frame))
                        await IngestAsync(broadcasterId, sourceKey, payload, ct);
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
                    "Supporter socket for {Source} on {Channel} dropped — reconnecting in {Backoff}.",
                    sourceKey,
                    broadcasterId,
                    backoff
                );
                await MarkErrorAsync(connectionId, ct);
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
        string sourceKey,
        string payload,
        CancellationToken ct
    )
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ISupporterIngestService ingest =
                scope.ServiceProvider.GetRequiredService<ISupporterIngestService>();
            Result ingested = await ingest.IngestAsync(broadcasterId, sourceKey, payload, ct);
            if (ingested.IsFailure)
                _logger.LogWarning(
                    "Supporter socket ingest failed for {Source} on {Channel}: {Error}",
                    sourceKey,
                    broadcasterId,
                    ingested.ErrorMessage
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Supporter socket ingest crashed for {Source} on {Channel}.",
                sourceKey,
                broadcasterId
            );
        }
    }

    private async Task MarkErrorAsync(Guid connectionId, CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            SupporterConnection? connection = await db
                .SupporterConnections.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == connectionId, ct);
            if (connection is null)
                return;
            connection.Status = "error";
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not persist socket error status for {Id}.", connectionId);
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

    /// <summary>A live per-connection receive loop + the credential identity it was started with (restart signal).</summary>
    private sealed record Runner(
        string CredentialFingerprint,
        CancellationTokenSource Cts,
        Task Loop
    );
}
