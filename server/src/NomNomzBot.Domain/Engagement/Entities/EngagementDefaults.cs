// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
