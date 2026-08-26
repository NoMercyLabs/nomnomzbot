// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
