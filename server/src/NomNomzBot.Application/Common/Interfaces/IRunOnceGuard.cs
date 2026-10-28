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
/// The cluster-singleton primitive (platform-conventions §3.8). Gates hosted-service work that must fire exactly
/// once across a multi-node SaaS cluster (migrate / seed / conduit-provision). On lite the lease is always
/// granted (a single process); on full/SaaS it is a Postgres advisory lock so exactly one instance proceeds.
/// </summary>
public interface IRunOnceGuard
{
    /// <summary>
    /// Tries to acquire a named lease. Returns a disposable lease on success (released on dispose), or <c>null</c>
    /// when another instance currently holds it. Lite: always granted. SaaS: <c>pg_try_advisory_lock</c>.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceName,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    );
}
