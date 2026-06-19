// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;

namespace NomNomzBot.Application.Platform.Services;

/// <summary>
/// Application service for per-channel feature toggles (the <c>ChannelFeature</c> engine entity).
/// </summary>
public interface IFeatureService
{
    /// <summary>List the feature toggles configured for a channel.</summary>
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
}
