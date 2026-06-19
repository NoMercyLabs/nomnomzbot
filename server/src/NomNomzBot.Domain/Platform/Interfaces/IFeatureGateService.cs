// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Interfaces;

/// <summary>
/// Service for checking whether a specific feature is enabled for a channel.
/// </summary>
public interface IFeatureGateService
{
    Task<bool> IsEnabledAsync(
        string broadcasterId,
        string featureKey,
        CancellationToken cancellationToken = default
    );
}
