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
