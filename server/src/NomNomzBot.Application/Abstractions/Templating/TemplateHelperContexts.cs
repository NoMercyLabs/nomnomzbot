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

/// <summary>Named sets of <see cref="TemplateHelperContext"/> values that more than one helper row uses.</summary>
public static class TemplateHelperContexts
{
    /// <summary>
    /// Every surface a broadcaster writes a template for. It leaves out <see cref="TemplateHelperContext.Viewer"/>:
    /// the viewer surface is opt-in per helper, so a new helper is never open to viewer text by default.
    /// </summary>
    public static IReadOnlyList<TemplateHelperContext> Standard { get; } =
    [.. Enum.GetValues<TemplateHelperContext>().Where(c => c != TemplateHelperContext.Viewer)];

    /// <summary><see cref="Standard"/> plus the viewer surface, for a helper that is safe in a viewer's own text.</summary>
    public static IReadOnlyList<TemplateHelperContext> StandardAndViewer { get; } =
    [.. Enum.GetValues<TemplateHelperContext>()];
}
