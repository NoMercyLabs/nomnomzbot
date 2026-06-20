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
