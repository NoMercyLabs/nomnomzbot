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

    // Non-null only when the current request carries an act-as (impersonation) access token — i.e. the
    // JWT's `act` claim is present alongside `sid`. An impersonation token's `sub`/`UserId` IS the
    // impersonated SUBJECT (it grants exactly their access, S089a), so this surfaces the true OPERATOR
    // + session ambiently for the request, letting every journalled write during it attribute both
    // actors without every call site threading impersonation state through by hand.
    ImpersonationContext? Impersonation { get; }
}

/// <summary>
/// The ambient act-as (impersonation) context for the current request, read once from the access
/// token's <c>act</c>/<c>sid</c> claims. <paramref name="OperatorUserId"/> is the platform operator who
/// started the support session; <paramref name="SubjectUserId"/> is the impersonated user whose access
/// the token actually grants (identity-auth §3.6 — <c>act</c> is purely informational for authorization).
/// </summary>
public sealed record ImpersonationContext(Guid OperatorUserId, Guid SubjectUserId, Guid SessionId);
