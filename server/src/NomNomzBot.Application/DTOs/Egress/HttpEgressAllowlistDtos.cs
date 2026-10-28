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

namespace NomNomzBot.Application.DTOs.Egress;

/// <summary>One approved outbound host of the channel (egress allowlist). Only the host and its on/off switch are exposed.</summary>
public sealed record HttpEgressAllowlistDto(
    Guid Id,
    string Host,
    bool IsEnabled,
    Guid? ApprovedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Approve a new outbound host: a bare lowercase DNS name, no scheme, port, path or wildcard.</summary>
public sealed record CreateHttpEgressAllowlistRequest
{
    public required string Host { get; init; }
}

/// <summary>Switch an approved host on or off.</summary>
public sealed record SetHttpEgressAllowlistEnabledRequest
{
    public required bool IsEnabled { get; init; }
}
