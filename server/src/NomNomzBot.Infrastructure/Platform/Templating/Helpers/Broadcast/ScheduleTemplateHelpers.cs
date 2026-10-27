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

/// <summary>The <c>schedule</c> namespace (catalog section 7.11): the next stream, the week, a break and the links.</summary>
internal sealed class ScheduleTemplateHelpers(IServiceScopeFactory scopes)
    : BroadcastHelperProvider<ScheduleData>([
        new ScheduleNextHelpers(),
        new ScheduleListHelpers(),
        new ScheduleVacationHelpers(),
        new ScheduleLinkHelpers(),
    ])
{
    public override string Namespace => "schedule";

    protected override ScheduleData? DataFor(TemplateRenderScope scope) =>
        ScheduleData.For(scope, scopes);
}
