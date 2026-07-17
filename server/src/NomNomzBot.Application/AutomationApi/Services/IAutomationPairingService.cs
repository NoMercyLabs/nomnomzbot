// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.AutomationApi.Services;

/// <summary>
/// Generic device pairing over the automation API (stream-deck.md §3): the dashboard mints a
/// short-lived single-use code; the device redeems it once and receives a freshly-minted scoped
/// automation token — no copy-pasting a secret or URL into a device. Revoking the token unpairs.
/// </summary>
public interface IAutomationPairingService
{
    /// <summary>Mint a pairing code for the channel (cached ~5 min, single-use).</summary>
    Task<Result<PairingCodeDto>> MintCodeAsync(
        Guid broadcasterId,
        Guid actorUserId,
        MintPairingCodeRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Redeem a code exactly once: consumes it and mints the device's automation token.
    /// <paramref name="clientKey"/> partitions the brute-force guard (the caller's IP);
    /// <paramref name="backendUrl"/> is the public origin the device should connect back to.
    /// </summary>
    Task<Result<PairingRedemptionDto>> RedeemCodeAsync(
        string code,
        DeviceInfo device,
        string clientKey,
        string backendUrl,
        CancellationToken ct = default
    );
}
