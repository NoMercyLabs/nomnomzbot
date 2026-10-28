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
