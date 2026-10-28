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
