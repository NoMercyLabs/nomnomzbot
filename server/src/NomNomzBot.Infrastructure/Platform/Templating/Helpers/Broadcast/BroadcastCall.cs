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
