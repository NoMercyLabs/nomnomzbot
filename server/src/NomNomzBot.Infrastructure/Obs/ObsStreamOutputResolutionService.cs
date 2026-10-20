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
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs;

/// <summary>
/// Reads the OBS output resolution (obs-websocket v5 <c>GetVideoSettings</c>: <c>outputWidth</c> x
/// <c>outputHeight</c>, the size of the stream the viewers get, not the canvas). Best effort: OBS not connected, a
/// rejected request or a missing size reads as null and never fails the caller.
/// </summary>
public sealed class ObsStreamOutputResolutionService(IObsTransport transport)
    : IStreamOutputResolutionService
{
    public async Task<StreamOutputResolution?> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Result<Application.Obs.Dtos.ObsResponse> response = await transport.SendAsync(
            broadcasterId,
            Guid.CreateVersion7(),
            new("GetVideoSettings", null),
            cancellationToken
        );
        if (response.IsFailure || !response.Value.Ok)
            return null;

        IReadOnlyDictionary<string, object?>? data = response.Value.ResponseData;
        int width = data?.GetValueOrDefault("outputWidth") is double w ? (int)w : 0;
        int height = data?.GetValueOrDefault("outputHeight") is double h ? (int)h : 0;

        return width > 0 && height > 0 ? new StreamOutputResolution(width, height) : null;
    }
}
