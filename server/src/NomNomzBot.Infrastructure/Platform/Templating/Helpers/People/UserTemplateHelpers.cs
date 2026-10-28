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

/// <summary>The <c>user.*</c> rows: the person who triggered the template.</summary>
internal sealed class UserTemplateHelpers : PeopleHelperProvider
{
    public UserTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, new DbViewerDirectory()) { }

    /// <summary>The second constructor lets a host (a test) register its own <see cref="IViewerDirectory"/>.</summary>
    public UserTemplateHelpers(IServiceScopeFactory scopes, IViewerDirectory directory)
        : base(scopes, directory, ViewerSide.User, "user") { }
}
