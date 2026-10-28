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
