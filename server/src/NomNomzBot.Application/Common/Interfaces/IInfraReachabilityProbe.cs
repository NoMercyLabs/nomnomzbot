// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
