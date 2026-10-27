// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

/// <summary>A small set of related <c>list.*</c> rows. The provider finds every group by reflection.</summary>
internal interface IListHelperGroup
{
    /// <summary>The registry rows this group owns, keyed by canonical <c>list.*</c> names.</summary>
    IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>The rendered text of the node; null when the key is not this group's or an argument is bad.</summary>
    ValueTask<string?> TryRenderAsync(
        HelperNode node,
        ListSource source,
        CancellationToken cancellationToken
    );
}
