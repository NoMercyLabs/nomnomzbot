// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
