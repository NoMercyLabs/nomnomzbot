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

namespace NomNomzBot.Application.Dashboard.Services;

/// <summary>The pixel size of the stream the viewers get (OBS output resolution).</summary>
public sealed record StreamOutputResolution(int Width, int Height);

/// <summary>Reads the output resolution of the channel's live stream so a recap can be saved at that size later.</summary>
public interface IStreamOutputResolutionService
{
    /// <summary>The channel's current output resolution, or null when it cannot be read (OBS not connected).</summary>
    Task<StreamOutputResolution?> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
