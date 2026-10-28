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

namespace NomNomzBot.Application.Common.Interfaces;

/// <summary>
/// A fast, non-throwing reachability probe for the durable data tier, used by the deployment-profile detector to
/// decide lite vs full at boot. Returns <c>false</c> (never throws / never blocks long) when the service is absent
/// — that is the normal lite case. Abstracted so the detection logic is unit-testable with deterministic inputs.
/// </summary>
public interface IInfraReachabilityProbe
{
    Task<bool> IsPostgresReachableAsync(CancellationToken cancellationToken = default);
    Task<bool> IsRedisReachableAsync(CancellationToken cancellationToken = default);
}
