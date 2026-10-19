// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
