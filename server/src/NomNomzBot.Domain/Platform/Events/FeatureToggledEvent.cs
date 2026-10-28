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

namespace NomNomzBot.Domain.Platform.Events;

/// <summary>When a feature is switched on or off for the channel.</summary>
public sealed class FeatureToggledEvent : DomainEventBase
{
    /// <summary>The key of the feature that was switched.</summary>
    public required string FeatureKey { get; init; }

    /// <summary>True when the feature is now on. False when it is now off.</summary>
    public required bool Enabled { get; init; }
}
