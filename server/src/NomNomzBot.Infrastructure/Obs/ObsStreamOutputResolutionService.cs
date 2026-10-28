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
