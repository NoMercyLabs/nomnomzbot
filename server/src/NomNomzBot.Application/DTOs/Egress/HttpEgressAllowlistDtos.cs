// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
