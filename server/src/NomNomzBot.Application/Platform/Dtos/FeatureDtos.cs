// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Platform.Dtos;

/// <summary>
/// The enablement status of a single channel feature. Two independent axes the client must not conflate:
/// <see cref="IsEnabled"/> is the channel's OWN opt-in choice (row on, or the catalogue default), while
/// <see cref="Entitled"/> is whether the channel's tier / deployment / platform flag even ALLOWS the feature. A
/// feature is actually usable iff <c>Entitled &amp;&amp; IsEnabled</c>; a not-entitled feature can still read
/// opt-in ON (legacy state), so the dashboard shows "Upgrade to unlock" (see <see cref="RequiredTier"/>) rather
/// than a toggle. An ungated feature is always <see cref="Entitled"/>.
/// </summary>
public record FeatureStatusDto(
    string FeatureKey,
    string Label,
    string Description,
    bool IsEnabled,
    DateTime? EnabledAt,
    string[] RequiredScopes,
    bool Entitled = true,
    string? EntitlementReason = null,
    string? RequiredTier = null
);
