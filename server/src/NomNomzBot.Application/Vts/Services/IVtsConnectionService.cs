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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Vts.Dtos;

namespace NomNomzBot.Application.Vts.Services;

/// <summary>
/// The channel's VTube Studio connection configuration (vtube-studio.md §3): mode, endpoint, sealed
/// plugin-token custody, and the rotatable bridge credential. The interactive authorize flow (the
/// one-time in-VTS approval that mints the plugin token) rides the transport layer in a later slice;
/// this surface only manages the stored configuration.
/// </summary>
public interface IVtsConnectionService
{
    /// <summary>The stored configuration, or the defaults when the channel has none yet (reads never write).</summary>
    Task<Result<VtsConnectionDto>> GetAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Create-or-update mode/endpoint/mask/enabled; the plugin token is untouched by upserts.</summary>
    Task<Result<VtsConnectionDto>> UpsertAsync(
        Guid broadcasterId,
        UpsertVtsConnectionRequest request,
        CancellationToken ct = default
    );

    /// <summary>Mint a fresh bridge token; the old one stops authenticating immediately.</summary>
    Task<Result<VtsConnectionDto>> RotateBridgeTokenAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>The sealed plugin token opened for the TRANSPORT only — never for an API response.</summary>
    Task<string?> GetPluginTokenForTransportAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Seals and stores a freshly-granted plugin token (called by the authorize flow).</summary>
    Task<Result> StorePluginTokenAsync(
        Guid broadcasterId,
        string pluginToken,
        CancellationToken ct = default
    );
}
