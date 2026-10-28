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

namespace NomNomzBot.Domain.Engagement.Entities;

/// <summary>
/// The one place each engagement default is written. A channel with no saved row resolves to these
/// values; the entity, the EF mapping, the service and the defaults endpoint all read them, so the
/// dashboard never carries a second copy.
/// </summary>
public static class EngagementDefaults
{
    /// <summary>Burst limiter for greetings, in seconds.</summary>
    public const int GreetCooldownSeconds = 5;
}
