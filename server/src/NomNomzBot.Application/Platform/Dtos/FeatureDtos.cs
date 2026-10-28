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
