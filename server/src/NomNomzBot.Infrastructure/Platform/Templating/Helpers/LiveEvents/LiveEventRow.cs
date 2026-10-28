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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The one memoized fetch of a live-event namespace for one render.</summary>
internal interface ILiveEventData;

internal readonly record struct LiveEventCall(
    ILiveEventData Data,
    IReadOnlyList<string> Args,
    TemplateRenderScope Scope
)
{
    public CultureInfo Culture => NumberCulture.For(Scope.Language);
}

/// <summary>
/// One helper form. A row reads its namespace's data through the typed factories of the namespace
/// (<see cref="LiveHypeTrainRows"/>, <see cref="LiveGoalRows"/>, <see cref="LiveCharityRows"/>) and prints
/// null when the channel has nothing live to read, so the token is written as typed.
/// </summary>
internal sealed record LiveEventRow(
    string Key,
    string Sample,
    string Namespace,
    Func<ILiveEventData, Task> Warm,
    Func<LiveEventCall, Task<string?>> Render
)
{
    public TemplateHelperEntry ToEntry() =>
        new(Key, TemplateHelperContexts.Standard, new LocalizedText("template.helper." + Key))
        {
            Sample = Sample,
            Source = Namespace,
            Freshness = TemplateHelperFreshness.Cached,
            Scope = TemplateHelperScope.Channel,
        };
}
