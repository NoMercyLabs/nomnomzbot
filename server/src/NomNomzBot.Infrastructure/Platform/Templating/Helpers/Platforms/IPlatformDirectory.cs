// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
