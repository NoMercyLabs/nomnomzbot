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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// One responsibility inside the <c>text</c> namespace (case, spacing, cutting, padding, word picks, ...).
/// The registry allows one <see cref="ITemplateHelperProvider"/> per namespace, so
/// <see cref="TextTemplateHelpers"/> is the only provider and it is a thin aggregator over these groups.
///
/// To add text helpers, add ONE new file with a class that derives from <see cref="TextHelperGroup"/>, has a
/// parameterless constructor and passes its <see cref="TextHelperRow"/> list to the base. The provider finds it
/// by reflection: no shared file changes. Keep a group pure (string in, string out) and small.
/// </summary>
internal interface ITextHelperGroup
{
    /// <summary>The registry rows this group owns, keyed by canonical <c>text.*</c> names.</summary>
    IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>The rendered text of the node; null when the key is not this group's or an argument is bad.</summary>
    string? TryRender(HelperNode node, TemplateRenderScope scope);
}
