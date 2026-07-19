// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;

namespace NomNomzBot.Application.Obs.Services;

/// <summary>
/// The channel's OBS connection configuration (obs-control.md §3): mode, endpoint, sealed password
/// custody, and the rotatable bridge credential. Live control and connection testing ride the
/// transport layer (a later slice); this surface only manages the stored configuration.
/// </summary>
public interface IObsConnectionService
{
    /// <summary>The stored configuration, or the defaults when the channel has none yet (reads never write).</summary>
    Task<Result<ObsConnectionDto>> GetAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Create-or-update; the password is write-only (null keeps, empty clears) and is sealed at rest.</summary>
    Task<Result<ObsConnectionDto>> UpsertAsync(
        Guid broadcasterId,
        UpsertObsConnectionRequest request,
        CancellationToken ct = default
    );

    /// <summary>Mint a fresh bridge token; the old one stops authenticating immediately.</summary>
    Task<Result<ObsBridgeSetupDto>> RotateBridgeTokenAsync(
        Guid broadcasterId,
        string backendUrl,
        CancellationToken ct = default
    );

    /// <summary>The bridge install URL (minting a token on first ask so setup is one click).</summary>
    Task<Result<ObsBridgeSetupDto>> GetBridgeSetupAsync(
        Guid broadcasterId,
        string backendUrl,
        CancellationToken ct = default
    );

    /// <summary>
    /// The sealed OBS-WS password opened for a TRANSPORT — the direct socket, or the bridge relay push that
    /// hands it to the browser-source bridge over the authenticated hub. Never returned on an API response.
    /// </summary>
    Task<string?> GetPasswordForTransportAsync(Guid broadcasterId, CancellationToken ct = default);
}
