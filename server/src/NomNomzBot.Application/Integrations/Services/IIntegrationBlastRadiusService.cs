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

using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Integrations.Services;

/// <summary>
/// Counts what STOPS WORKING when an external provider is disconnected (S-CONSEQ). Disconnecting is the
/// sharpest destructive action in the product precisely because it deletes almost nothing: the rows survive
/// and simply stop functioning, so a preview that counted only deleted rows would report a reassuring zero
/// while every Spotify command in the channel went dead. This counts the dependents instead — the pipeline
/// steps whose action belongs to that provider, and the supporter feeds that ingest through its connection.
/// </summary>
public interface IIntegrationBlastRadiusService
{
    /// <summary>
    /// The real, counted blast radius of disconnecting <paramref name="integrationId"/> (the provider key the
    /// disconnect endpoint takes, e.g. <c>spotify</c>). Returns a failure when the channel has no connection
    /// for that provider — a provider that is not connected is a failed lookup, never a counted zero.
    /// </summary>
    Task<Result<BlastRadiusDto>> GetDisconnectBlastRadiusAsync(
        Guid broadcasterId,
        string integrationId,
        CancellationToken ct = default
    );
}
