// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
