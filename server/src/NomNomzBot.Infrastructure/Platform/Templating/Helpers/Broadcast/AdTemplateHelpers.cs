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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The <c>ad</c> namespace (catalog section 7.11): the next and last ad break and the snooze allowance.</summary>
internal sealed class AdTemplateHelpers(IServiceScopeFactory scopes)
    : BroadcastHelperProvider<AdData>([new AdNextHelpers(), new AdPastAndSnoozeHelpers()])
{
    public override string Namespace => "ad";

    protected override AdData? DataFor(TemplateRenderScope scope) => AdData.For(scope, scopes);
}
