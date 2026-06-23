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
