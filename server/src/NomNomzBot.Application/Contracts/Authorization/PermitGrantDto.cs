// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// A <c>!permit</c> grant for the permissions UI / audit (roles-permissions §4) — a per-user role grant or a
/// per-user capability grant (an action key), with its grantor, optional expiry, and revocation state.
/// </summary>
public sealed record PermitGrantDto(
    Guid Id,
    Guid UserId,
    string? Username,
    PermitGrantType GrantType,
    ManagementRole? GrantedRole,
    string? CapabilityActionKey,
    Guid GrantedByUserId,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    string? Reason,
    DateTime CreatedAt
);
