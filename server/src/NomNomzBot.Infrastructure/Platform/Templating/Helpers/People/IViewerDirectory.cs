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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>Finds who the viewer or the target of a render is. One lookup per side and render.</summary>
internal interface IViewerDirectory
{
    /// <summary>The person, or null when the render's seeds name nobody for this side.</summary>
    Task<ViewerRef?> ResolveAsync(
        IServiceProvider services,
        ViewerSide side,
        IReadOnlyDictionary<string, string> seeds,
        CancellationToken cancellationToken
    );
}
