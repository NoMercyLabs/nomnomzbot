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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>
/// One responsibility inside the <c>args</c> namespace (whole-text forms, picks and ranges, single words, parsing).
/// The registry allows one provider per namespace, so <see cref="ArgumentTemplateHelpers"/> is a thin aggregator
/// over these groups.
///
/// To add argument helpers, add ONE new file with a class that derives from <see cref="ArgumentHelperGroup"/>, has a
/// parameterless constructor and passes its <see cref="ArgumentRow"/> list to the base. The provider finds it by
/// reflection: no shared file changes. Keep a group pure (it reads only the <see cref="CommandWords"/>).
/// </summary>
internal interface IArgumentHelperGroup
{
    /// <summary>The registry rows this group owns.</summary>
    IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>The rendered text of the node; null when the key is not this group's or an argument is bad.</summary>
    string? TryRender(HelperNode node, CommandWords words);
}
