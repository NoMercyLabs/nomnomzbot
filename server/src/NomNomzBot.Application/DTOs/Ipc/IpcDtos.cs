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
