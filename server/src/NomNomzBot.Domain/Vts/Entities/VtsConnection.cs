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
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>One VTS connection per channel (unique).</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>Transport plane: <c>direct</c> | <c>bridge</c>.</summary>
    [MaxLength(20)]
    public string Mode { get; set; } = VtsConnectionDefaults.Mode;

    /// <summary>The VTube Studio API endpoint for direct mode.</summary>
    [MaxLength(200)]
    public string Endpoint { get; set; } = VtsConnectionDefaults.Endpoint;

    /// <summary>The VTS plugin auth token as a sealed envelope — never plaintext, never echoed.</summary>
    public string? PluginTokenCipher { get; set; }

    /// <summary>The bridge's connect credential (shares the OBS relay); rotatable, unique platform-wide.</summary>
    [MaxLength(64)]
    public string? BridgeToken { get; set; }

    /// <summary>Bitmask of subscribed VTS event groups (vtube-studio.md §4).</summary>
    public int EventSubscriptionsMask { get; set; } = VtsConnectionDefaults.EventSubscriptionsMask;

    public bool IsEnabled { get; set; } = VtsConnectionDefaults.IsEnabled;

    /// <summary>Auth/connection state: <c>unauthorized</c> | <c>authorized</c> | <c>connected</c> | <c>error</c>.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = VtsConnectionDefaults.Status;

    public DateTime? LastConnectedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
