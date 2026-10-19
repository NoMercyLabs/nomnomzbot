// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Vts.Entities;

/// <summary>
/// The one place each VTube Studio connection default is written. A channel with no saved row resolves to
/// these values; the entity, the service and the API all read them.
/// </summary>
public static class VtsConnectionDefaults
{
    /// <summary>Transport plane: <c>direct</c> | <c>bridge</c>.</summary>
    public const string Mode = "direct";

    /// <summary>The VTube Studio API endpoint for direct mode.</summary>
    public const string Endpoint = "ws://localhost:8001";

    /// <summary>Default event-subscription mask: the low-volume event set (vtube-studio.md §4).</summary>
    public const int EventSubscriptionsMask = 0xFF;

    /// <summary>The highest mask the API accepts: one bit per VTS event, bits 0–10 (vtube-studio.md §4).</summary>
    public const int MaxEventSubscriptionsMask = 0x7FF;

    public const bool IsEnabled = false;

    /// <summary>Auth/connection state of a connection that was never authorized.</summary>
    public const string Status = "unauthorized";
}
