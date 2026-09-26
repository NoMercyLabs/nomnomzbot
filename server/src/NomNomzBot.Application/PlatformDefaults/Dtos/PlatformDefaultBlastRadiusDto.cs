// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.PlatformDefaults.Dtos;

/// <summary>
/// The counted consequence of changing one platform default, shown BEFORE the save (consequences must be
/// visible). <paramref name="ChannelsAffected"/> is the active channels that follow the platform default and
/// would therefore feel the change (0 when the proposed value equals the current one);
/// <paramref name="ChannelsKeepingOwnSetting"/> is the active channels whose own setting keeps winning.
/// <paramref name="RequiresDangerConfirmation"/> is true when the default guards a dangerous action, so the
/// save must carry an explicit confirmation. The save echoes <paramref name="ChannelsAffected"/> back and is
/// refused as stale when the live count has moved since.
/// </summary>
public sealed record PlatformDefaultBlastRadiusDto(
    int ChannelsAffected,
    int ChannelsKeepingOwnSetting,
    IReadOnlyList<string> SampleChannelNames,
    bool RequiresDangerConfirmation
);
