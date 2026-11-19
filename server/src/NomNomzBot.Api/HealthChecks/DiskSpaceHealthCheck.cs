// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Api.HealthChecks;

/// <summary>
/// Free space on the disk that holds the bot's data folder. Under <see cref="WarnBelowGb"/> it reports
/// Degraded. Only the operator can free host disk, so the signal reaches the admin console's system health
/// panel only, never a streamer. It is never tagged <c>ready</c>: a low disk warns, it does not take the
/// instance out of rotation.
/// </summary>
public sealed class DiskSpaceHealthCheck(Func<long> freeBytes) : IHealthCheck
{
    public const long WarnBelowGb = 10;
    private const long BytesPerGb = 1024L * 1024 * 1024;

    public DiskSpaceHealthCheck()
        : this(() => new DriveInfo(SelfHostDataPaths.BaseDirectory).AvailableFreeSpace) { }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        long freeGb;
        try
        {
            freeGb = freeBytes() / BytesPerGb;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Task.FromResult(
                HealthCheckResult.Degraded($"Free disk space unreadable: {ex.Message}")
            );
        }

        Dictionary<string, object> data = new() { ["freeGb"] = freeGb };
        return Task.FromResult(
            freeGb < WarnBelowGb
                ? HealthCheckResult.Degraded(
                    $"Only {freeGb} GB free on the data disk. Clear old images or logs before it fills.",
                    data: data
                )
                : HealthCheckResult.Healthy($"{freeGb} GB free on the data disk.", data)
        );
    }
}
