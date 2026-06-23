// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// Advertises the bot on the local link as <c>_nomnomz._tcp</c> via mDNS/DNS-SD (deployment-distribution §6) so the
/// native dashboard's LAN discovery finds it — including the actual bound port, which smart port resolution may have
/// changed. <b>Self-host only</b>: it is registered solely when the resolved profile is <c>self_host_lite</c> /
/// <c>self_host_full</c> (SaaS has no LAN to discover on, so the service is simply not added).
/// <para>
/// It waits for the host to publish its bound port (<see cref="IListenEndpointAccessor"/>) before announcing — the
/// port is known only after the listener binds — then opens the multicast socket, advertises, and re-announces on
/// network-interface change. Shutdown unadvertises cleanly so a stale record doesn't linger on the LAN.
/// </para>
/// </summary>
public sealed class MdnsAdvertiserHostedService : IHostedService, IDisposable
{
    // How often to re-check the bound port while the host is still finishing startup.
    private static readonly TimeSpan PortPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PortWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly IListenEndpointAccessor _endpoint;
    private readonly IDeploymentProfileService _profile;
    private readonly ILogger<MdnsAdvertiserHostedService> _logger;

    private MulticastService? _mdns;
    private ServiceDiscovery? _discovery;
    private ServiceProfile? _serviceProfile;
    private CancellationTokenSource? _startupCts;
    private Task? _startupTask;

    public MdnsAdvertiserHostedService(
        IListenEndpointAccessor endpoint,
        IDeploymentProfileService profile,
        ILogger<MdnsAdvertiserHostedService> logger
    )
    {
        _endpoint = endpoint;
        _profile = profile;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Don't block host startup on the port being bound: kick off the wait-then-advertise on a background task.
        _startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _startupTask = Task.Run(
            () => WaitThenAdvertiseAsync(_startupCts.Token),
            CancellationToken.None
        );
        return Task.CompletedTask;
    }

    private async Task WaitThenAdvertiseAsync(CancellationToken cancellationToken)
    {
        try
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + PortWaitTimeout;
            while (!_endpoint.IsResolved && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(PortPollInterval, cancellationToken);

            if (!_endpoint.IsResolved)
            {
                _logger.LogWarning(
                    "mDNS advertiser: the host did not publish a bound port within {Timeout}; LAN discovery is "
                        + "unavailable this run.",
                    PortWaitTimeout
                );
                return;
            }

            int port = _endpoint.Port;
            Guid instanceId = _profile.Current.InstanceId;

            _serviceProfile = MdnsServiceProfileFactory.Build(
                instanceId,
                Environment.MachineName,
                port
            );

            _mdns = new MulticastService();
            _discovery = new ServiceDiscovery(_mdns);

            // Re-announce when a network interface appears (Wi-Fi reconnect, VPN up/down), so the bot stays findable.
            _mdns.NetworkInterfaceDiscovered += (_, _) =>
            {
                if (_serviceProfile is { } sp)
                    _discovery?.Announce(sp);
            };

            _mdns.Start();
            _discovery.Advertise(_serviceProfile);

            _logger.LogInformation(
                "Advertising {ServiceType} on the LAN: instance '{Instance}' ({InstanceId}) on port {Port}.",
                MdnsServiceProfileFactory.ServiceType,
                _serviceProfile.InstanceName,
                instanceId,
                port
            );
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down before we finished starting — nothing to advertise.
        }
        catch (Exception ex)
        {
            // mDNS is a convenience (the native app can still connect by manual URL); never let it fail the host.
            _logger.LogError(
                ex,
                "mDNS advertiser failed to start; LAN auto-discovery is unavailable this run (manual connect still works)."
            );
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_startupCts is not null)
            await _startupCts.CancelAsync();

        if (_startupTask is not null)
        {
            try
            {
                await _startupTask;
            }
            catch
            {
                // Already logged inside the task; nothing actionable on stop.
            }
        }

        try
        {
            if (_serviceProfile is { } sp)
                _discovery?.Unadvertise(sp);
            _mdns?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "mDNS advertiser cleanup on shutdown was not clean.");
        }
    }

    public void Dispose()
    {
        _startupCts?.Dispose();
        _discovery?.Dispose();
        _mdns?.Dispose();
    }
}
