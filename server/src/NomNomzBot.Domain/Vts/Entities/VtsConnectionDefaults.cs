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
