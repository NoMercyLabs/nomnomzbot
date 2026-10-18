// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// Gives the variable picker real examples: for the helpers in <see cref="TemplateHelperSamples.LiveKeys"/>
/// the example is what the template engine renders for the signed-in user and the current channel, so the
/// "Becomes ..." hint shows the viewer their own name instead of a made-up one.
/// </summary>
public interface ITemplateSampleRenderer
{
    /// <summary>
    /// Returns <paramref name="entries"/> with <see cref="TemplateHelperEntry.Sample"/> replaced by the real
    /// value where the engine can produce one from stored data. An entry the engine cannot resolve, or
    /// resolves to nothing, keeps its neutral sample. Never calls an external API.
    /// </summary>
    Task<IReadOnlyList<TemplateHelperEntry>> PersonalizeAsync(
        IReadOnlyList<TemplateHelperEntry> entries,
        Guid broadcasterId,
        Guid userId,
        CancellationToken cancellationToken = default
    );
}
