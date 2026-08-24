// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Mints (idempotently) a real <c>IamPrincipal</c> + <c>platform-super-admin</c> role assignment for a
/// user, attributed to the <c>system-bootstrap</c> service-account principal (D2 bootstrap fix). Shared by
/// two callers: <c>AuthService</c> mints at promotion time for a freshly promoted
/// <c>IsPlatformPrincipal</c> user, and the startup backfill seeder mints for pre-existing
/// <c>IsPlatformPrincipal</c> users that were promoted before the D2 fix landed and never got a principal
/// row. Both callers get the identical, idempotent row-building logic — never duplicated.
/// </summary>
public interface IPlatformOwnerPrincipalMinter
{
    /// <summary>
    /// Ensures <paramref name="userId"/> has an active <c>IamPrincipal</c> and an unrevoked
    /// <c>platform-super-admin</c> role assignment. Idempotent: re-running for an already-minted user adds
    /// neither a duplicate principal nor a duplicate assignment. Does not call <c>SaveChanges</c> — the
    /// caller owns the transaction boundary.
    /// </summary>
    Task MintAsync(Guid userId, CancellationToken ct = default);
}
