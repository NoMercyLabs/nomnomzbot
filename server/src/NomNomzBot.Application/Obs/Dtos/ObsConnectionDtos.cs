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

namespace NomNomzBot.Application.Obs.Dtos;

/// <summary>
/// A channel's OBS connection as the dashboard sees it (obs-control.md §3). The OBS-WS password never
/// appears — only <see cref="HasPassword"/> — and the bridge token is surfaced solely through the
/// bridge-setup URL, never as a bare listing field.
/// </summary>
public sealed record ObsConnectionDto(
    string Mode,
    string? Host,
    int? Port,
    bool HasPassword,
    bool HasBridgeToken,
    int EventSubscriptionsMask,
    bool IsEnabled,
    DateTime? LastConnectedAt,
    string? LastError
);

/// <summary>Upsert request; <see cref="Password"/> is WRITE-ONLY (null = keep, empty string = clear).</summary>
public sealed record UpsertObsConnectionRequest
{
    /// <summary>Transport plane: <c>direct</c> | <c>bridge</c>.</summary>
    [Required]
    [RegularExpression("^(direct|bridge)$")]
    public string Mode { get; init; } = null!;

    [MaxLength(255)]
    public string? Host { get; init; }

    [Range(1, 65535)]
    public int? Port { get; init; }

    /// <summary>OBS-WebSocket password — sealed at rest, never echoed back.</summary>
    [MaxLength(255)]
    public string? Password { get; init; }

    public int? EventSubscriptionsMask { get; init; }

    public bool IsEnabled { get; init; }
}

/// <summary>Everything the operator needs to install the browser-source bridge in OBS.</summary>
public sealed record ObsBridgeSetupDto(string BridgeUrl);

/// <summary>
/// The truthful result of actively probing OBS reachability (obs-control.md §3.2). The passive
/// state/scenes/inputs reads mask a "not connected yet" as an empty 200 so the dashboard shows its connect
/// prompt instead of a scary 500 — which means a 200 there is NOT proof OBS is reachable. This probe instead
/// ATTEMPTS the transport and reports the real outcome: <see cref="Connected"/>, plus the failing
/// <see cref="ErrorCode"/> (<c>OBS_NOT_CONNECTED</c>, <c>OBS_DISABLED</c>, <c>OBS_BRIDGE_OFFLINE</c>,
/// <c>OBS_WRONG_MODE</c>) when it could not reach OBS. It is what lets the dashboard tell "connected" from the
/// masked empty state — for the direct socket and the bridge leader alike.
/// </summary>
public sealed record ObsProbeDto(bool Connected, string? ErrorCode, string? Error);
