// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Vts.Entities;

/// <summary>
/// A channel's VTube Studio connection (vtube-studio.md §1, schema P.19) — one row per channel.
/// <see cref="Mode"/> selects the transport plane (<c>direct</c> — the server dials the VTS API at
/// <see cref="Endpoint"/>; <c>bridge</c> — the OBS browser-source relay proxies localhost VTS).
/// The VTS plugin token — granted ONCE by the streamer approving the in-VTS popup — is stored only
/// as a sealed envelope (<see cref="PluginTokenCipher"/>) and replayed at session start; it is never
/// readable through any API. <see cref="Status"/> tracks the auth/connection state machine:
/// <c>unauthorized</c> → <c>authorized</c> → <c>connected</c> / <c>error</c>.
/// </summary>
public class VtsConnection : SoftDeletableEntity, ITenantScoped
{
    /// <summary>Default event-subscription mask: the low-volume event set (vtube-studio.md §4).</summary>
    public const int DefaultEventSubscriptionsMask = 0xFF;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>One VTS connection per channel (unique).</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>Transport plane: <c>direct</c> | <c>bridge</c>.</summary>
    [MaxLength(20)]
    public string Mode { get; set; } = "direct";

    /// <summary>The VTube Studio API endpoint for direct mode.</summary>
    [MaxLength(200)]
    public string Endpoint { get; set; } = "ws://localhost:8001";

    /// <summary>The VTS plugin auth token as a sealed envelope — never plaintext, never echoed.</summary>
    public string? PluginTokenCipher { get; set; }

    /// <summary>The bridge's connect credential (shares the OBS relay); rotatable, unique platform-wide.</summary>
    [MaxLength(64)]
    public string? BridgeToken { get; set; }

    /// <summary>Bitmask of subscribed VTS event groups (vtube-studio.md §4).</summary>
    public int EventSubscriptionsMask { get; set; } = DefaultEventSubscriptionsMask;

    public bool IsEnabled { get; set; }

    /// <summary>Auth/connection state: <c>unauthorized</c> | <c>authorized</c> | <c>connected</c> | <c>error</c>.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "unauthorized";

    public DateTime? LastConnectedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
