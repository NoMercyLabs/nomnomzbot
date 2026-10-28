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
