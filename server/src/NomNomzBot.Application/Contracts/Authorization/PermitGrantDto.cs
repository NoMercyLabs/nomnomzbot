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
