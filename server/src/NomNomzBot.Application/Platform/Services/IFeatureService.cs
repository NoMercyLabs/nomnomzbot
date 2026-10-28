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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;

namespace NomNomzBot.Application.Platform.Services;

/// <summary>
/// Application service for per-channel feature toggles (the <c>ChannelFeature</c> engine entity).
/// </summary>
public interface IFeatureService
{
    /// <summary>List ALL known features for a channel, each with its current opt-in state.</summary>
    Task<Result<List<FeatureStatusDto>>> GetFeaturesAsync(
        string channelId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Toggle a feature for a channel, creating it (enabled) if it does not yet exist.</summary>
    Task<Result<FeatureStatusDto>> ToggleFeatureAsync(
        string channelId,
        string featureKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns true when the channel has opted-in and enabled <paramref name="featureKey"/>.</summary>
    Task<bool> IsFeatureEnabledAsync(
        string channelId,
        string featureKey,
        CancellationToken cancellationToken = default
    );
}
