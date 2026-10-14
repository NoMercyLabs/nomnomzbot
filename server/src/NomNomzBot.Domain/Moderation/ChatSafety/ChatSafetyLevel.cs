// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace NomNomzBot.Domain.Moderation.ChatSafety;

/// <summary>How safe a channel's chat is right now. The level only reports; it never acts.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatSafetyLevel
{
    /// <summary>Nothing unusual.</summary>
    Calm = 0,

    /// <summary>A possible cohort is forming, or a meter is over twice its baseline.</summary>
    Watch = 1,

    /// <summary>A campaign cohort is open, a lockdown or shield mode is on, or follows spike.</summary>
    Raid = 2,

    /// <summary>A raid ended less than five minutes ago.</summary>
    Over = 3,
}
