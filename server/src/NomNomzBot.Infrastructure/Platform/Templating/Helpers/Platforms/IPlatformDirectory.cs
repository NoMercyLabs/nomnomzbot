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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>Finds the platform connections of the streamer that owns the render's channel.</summary>
public interface IPlatformDirectory
{
    /// <summary>The connections in platform order (Twitch, Kick, YouTube), the render's own channel included.</summary>
    Task<IReadOnlyList<PlatformLink>> ListAsync(
        IServiceProvider services,
        Guid broadcasterId,
        CancellationToken cancellationToken
    );
}
