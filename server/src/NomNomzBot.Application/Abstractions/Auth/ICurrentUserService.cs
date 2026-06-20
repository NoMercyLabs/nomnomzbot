// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Auth;

public interface ICurrentUserService
{
    string? UserId { get; }
    string? Username { get; }
    bool IsAuthenticated { get; }

    // True when the principal is a platform operator/admin (identity-auth §3.6) — read from the JWT
    // `admin` role claim, sourced from User.IsPlatformPrincipal at login.
    bool IsPlatformPrincipal { get; }
}
