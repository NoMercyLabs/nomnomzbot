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

namespace NomNomzBot.Application.AutomationApi.Dtos;

/// <summary>Request to mint a device pairing code (stream-deck.md §3) — the dashboard side of pairing.</summary>
public sealed record MintPairingCodeRequest
{
    /// <summary>What the paired device will be called — becomes part of the minted token's name.</summary>
    [Required]
    [MaxLength(80)]
    public string DeviceLabel { get; init; } = null!;

    /// <summary>
    /// Scopes the paired device receives. Empty = the safe default (<c>invoke</c>, <c>events</c>,
    /// <c>read</c>); <c>chat</c> is granted ONLY when explicitly listed here.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];
}

/// <summary>A short-lived single-use pairing code the operator types (or scans) into the device.</summary>
public sealed record PairingCodeDto(string Code, DateTime ExpiresAt);

/// <summary>What the redeeming device says about itself (shown in the token list, e.g. "Stream Deck: Office").</summary>
public sealed record DeviceInfo
{
    /// <summary>Device family, e.g. <c>Stream Deck</c>, <c>Touch Portal</c>.</summary>
    [Required]
    [MaxLength(50)]
    public string Kind { get; init; } = null!;

    [MaxLength(80)]
    public string? Name { get; init; }
}

/// <summary>Body of <c>POST /automation/v1/pair</c> — the code IS the credential.</summary>
public sealed record RedeemPairingCodeRequest
{
    [Required]
    [MaxLength(16)]
    public string Code { get; init; } = null!;

    [Required]
    public DeviceInfo Device { get; init; } = null!;
}

/// <summary>
/// Everything a freshly-paired device needs to talk to this instance: where (<see cref="BackendUrl"/>),
/// as whom (<see cref="Token"/> — the one-time automation token secret, never retrievable again), and
/// what it may do (<see cref="Scopes"/>).
/// </summary>
public sealed record PairingRedemptionDto(
    string BackendUrl,
    string Token,
    IReadOnlyList<string> Scopes
);
