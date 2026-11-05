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

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Enums.Deployment;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Scoped (needs <see cref="IBillingTierService"/>, which is DbContext-backed) resolver for
/// <see cref="IPipelineConcurrencyLimitProvider"/>. Self-host never touches billing: it sizes itself from the
/// host's own hardware and database engine (<see cref="ScalingDefaults"/>). SaaS resolves the channel's tier
/// key and maps it through <see cref="ScalingOptions.SaasTierMaxConcurrentPerChannel"/> — briefly cached per
/// channel so a tier lookup is not on the hot path of every single command.
/// </summary>
public sealed class PipelineConcurrencyLimitProvider : IPipelineConcurrencyLimitProvider
{
    private static readonly TimeSpan TierCacheTtl = TimeSpan.FromMinutes(5);

    private readonly IDeploymentProfileService _deployment;
    private readonly IBillingTierService _billing;
    private readonly ScalingOptions _options;

    private readonly ConcurrentDictionary<Guid, (int Limit, DateTimeOffset ExpiresAt)> _tierCache =
        new();

    public PipelineConcurrencyLimitProvider(
        IDeploymentProfileService deployment,
        IBillingTierService billing,
        IOptions<ScalingOptions> options
    )
    {
        _deployment = deployment;
        _billing = billing;
        _options = options.Value;
    }

    public async Task<int> ResolveMaxConcurrentPerChannelAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        DeploymentProfileSnapshot? profile = TryReadProfile();
        if (profile is not { Mode: DeploymentMode.Saas })
            return ScalingDefaults.ResolveSelfHostMaxConcurrentPerChannel(
                profile?.DbProvider,
                _options.SelfHostMaxConcurrentPerChannel
            );

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (
            _tierCache.TryGetValue(broadcasterId, out (int Limit, DateTimeOffset ExpiresAt) cached)
            && cached.ExpiresAt > now
        )
            return cached.Limit;

        int limit = _options.SaasDefaultMaxConcurrentPerChannel;
        Result<EntitlementDto> entitlement = await _billing.GetEntitlementAsync(broadcasterId, ct);
        if (
            entitlement.IsSuccess
            && _options.SaasTierMaxConcurrentPerChannel.TryGetValue(
                entitlement.Value.TierKey,
                out int tierLimit
            )
        )
            limit = tierLimit;

        _tierCache[broadcasterId] = (limit, now.Add(TierCacheTtl));
        return limit;
    }

    private DeploymentProfileSnapshot? TryReadProfile()
    {
        try
        {
            return _deployment.Current;
        }
        catch (InvalidOperationException)
        {
            // Read before DetectAndPersistAsync resolved it (a pipeline fired before boot finished) —
            // fall back to the conservative self-host-lite default rather than throwing on the hot path.
            return null;
        }
    }
}
