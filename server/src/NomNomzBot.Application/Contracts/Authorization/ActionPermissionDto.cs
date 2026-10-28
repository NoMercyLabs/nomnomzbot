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
/// One row of a channel's per-action permission matrix (roles-permissions §4): the action's definition, its
/// global default/floor/tier, the channel's current override (if any), and the resulting effective required
/// level = <c>clamp(override ?? default, floor, Broadcaster)</c>. Drives the permissions screen.
/// </summary>
public sealed record ActionPermissionDto(
    Guid ActionDefinitionId,
    string ActionKey,
    AuthPlane Plane,
    string? Description,
    int DefaultLevel,
    int FloorLevel,
    DangerTier FloorTier,
    bool IsGrantableViaPermit,
    int? OverrideLevel,
    int EffectiveLevel
);
