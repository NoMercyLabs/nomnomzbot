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

namespace NomNomzBot.Application.PlatformDefaults.Dtos;

/// <summary>
/// One gateable action's platform default, as the admin editor shows it. <paramref name="ShippedDefaultLevel"/>
/// is the level the code ships; <paramref name="PlatformDefaultLevel"/> is the admin's replacement (null = none);
/// <paramref name="EffectiveDefaultLevel"/> is what every channel without an override actually enforces. Levels
/// are unified-ladder rung values — the dashboard renders them as role NAMES only.
/// </summary>
public sealed record ActionDefaultDto(
    string ActionKey,
    AuthPlane Plane,
    string? Description,
    int ShippedDefaultLevel,
    int? PlatformDefaultLevel,
    int EffectiveDefaultLevel,
    int FloorLevel,
    DangerTier FloorTier,
    int ChannelOverrideCount
);

/// <summary>
/// Sets (or, with a null <paramref name="Level"/>, clears) the platform default for one action.
/// <paramref name="ConfirmedChannelsAffected"/> must equal the preview's live count, and
/// <paramref name="ConfirmDanger"/> must be true for a Critical/ToS-tier action.
/// </summary>
public sealed record SetActionDefaultRequest(
    int? Level,
    int ConfirmedChannelsAffected,
    bool ConfirmDanger
);
