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
