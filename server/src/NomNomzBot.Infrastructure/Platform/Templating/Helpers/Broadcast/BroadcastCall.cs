// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>What a render of one ad, schedule or activity token needs.</summary>
internal readonly record struct BroadcastCall<TData>(
    TData Data,
    IReadOnlyList<string> Args,
    TemplateRenderScope Scope,
    CancellationToken Ct
)
{
    public DateTimeOffset Now => Scope.Now;

    public string? Timezone => Scope.Timezone;

    public CultureInfo Culture => NumberCulture.For(Scope.Language);
}
