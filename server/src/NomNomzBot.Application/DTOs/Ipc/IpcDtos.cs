// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.DTOs.Ipc;

/// <summary>Request to mint a local-IPC dev-mode key (stream-admin.md §4).</summary>
public sealed record CreateIpcKeyRequest(string? Label, DateTime? ExpiresAt);

/// <summary>
/// A local-IPC dev-mode key registry row (stream-admin.md §4). Metadata only — the stored SHA-256
/// hash never leaves the service. <see cref="PlaintextKey"/> is non-null ONLY in the create
/// response (the one time the secret exists outside the hash); null on every list/get thereafter.
/// </summary>
public sealed record IpcDevModeKeyDto(
    Guid Id,
    string? Label,
    bool IsEnabled,
    DateTime? ExpiresAt,
    DateTime CreatedAt,
    string? PlaintextKey
);
