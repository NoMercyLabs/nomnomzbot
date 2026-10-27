// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>The <c>target.*</c> rows: the first <c>@mention</c> of a command, read like <c>user.*</c>.</summary>
internal sealed class TargetTemplateHelpers : PeopleHelperProvider
{
    public TargetTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, new DbViewerDirectory()) { }

    /// <summary>The second constructor lets a host (a test) register its own <see cref="IViewerDirectory"/>.</summary>
    public TargetTemplateHelpers(IServiceScopeFactory scopes, IViewerDirectory directory)
        : base(scopes, directory, ViewerSide.Target, "target") { }
}
