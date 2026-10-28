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

namespace NomNomzBot.Domain.Obs.Entities;

/// <summary>
/// The one place each OBS connection default is written. A channel with no saved row resolves to these
/// values; the entity, the service and the API all read them.
/// </summary>
public static class ObsConnectionDefaults
{
    /// <summary>Transport plane: <c>direct</c> | <c>bridge</c>.</summary>
    public const string Mode = "direct";

    public const string Host = "127.0.0.1";

    /// <summary>The OBS-WebSocket default port.</summary>
    public const int Port = 4455;

    /// <summary>Default event-subscription mask: the low-volume default bits 0–11 (obs-control.md §6).</summary>
    public const int EventSubscriptionsMask = 0xFFF;

    /// <summary>
    /// The highest mask the API accepts: the general OBS groups (bits 0–10) plus the high-volume opt-ins
    /// (bits 16–19) fit inside 20 bits.
    /// </summary>
    public const int MaxEventSubscriptionsMask = 0xFFFFF;

    public const bool IsEnabled = false;
}
