// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

/// <summary>What a list helper sees: its rendered arguments, the channel's lists and the cancellation token.</summary>
internal sealed record ListHelperCall(
    IReadOnlyList<string> Args,
    ListSource Source,
    CancellationToken Ct
);

/// <summary>One <c>list.*</c> helper form: its registry row and how it renders. A null result prints the token as written.</summary>
internal sealed record ListHelperRow(
    string Key,
    string Sample,
    Func<ListHelperCall, ValueTask<string?>> Render,
    TemplateHelperFreshness Freshness,
    TemplateHelperScope Scope
)
{
    /// <summary>
    /// A form <c>{key:name|extra…}</c> over a named pick list. The list's items and the arguments after the
    /// name reach <paramref name="render"/>; a render with no channel or a bad argument count prints as written.
    /// </summary>
    public static ListHelperRow OfItems(
        string key,
        string sample,
        int extraMin,
        int extraMax,
        Func<IReadOnlyList<string>, IReadOnlyList<string>, string?> render
    ) =>
        new(
            key,
            sample,
            async call =>
            {
                bool fits =
                    call.Args.Count >= 1 + extraMin
                    && call.Args.Count <= 1 + extraMax
                    && call.Args[0].Length > 0;
                if (!fits || !call.Source.HasChannel)
                    return null;

                IReadOnlyList<string> items = await call.Source.ItemsAsync(call.Args[0], call.Ct);
                return render(items, [.. call.Args.Skip(1)]);
            },
            TemplateHelperFreshness.Live,
            TemplateHelperScope.Channel
        );

    /// <summary>A form <c>{key:name}</c> that asks the channel's lists for a value other than the items.</summary>
    public static ListHelperRow OfName(
        string key,
        string sample,
        Func<string, ListHelperCall, Task<string>> render
    ) =>
        new(
            key,
            sample,
            async call =>
                call.Args.Count == 1 && call.Args[0].Length > 0 && call.Source.HasChannel
                    ? await render(call.Args[0], call)
                    : null,
            TemplateHelperFreshness.Live,
            TemplateHelperScope.Channel
        );

    /// <summary>A form that computes from its own arguments only, so it works with no channel.</summary>
    public static ListHelperRow OfArguments(
        string key,
        string sample,
        Func<IReadOnlyList<string>, string?> render
    ) =>
        new(
            key,
            sample,
            call => ValueTask.FromResult(render(call.Args)),
            TemplateHelperFreshness.Pure,
            TemplateHelperScope.Global
        );

    public TemplateHelperEntry ToEntry() =>
        new(Key, TemplateHelperContexts.Standard, new LocalizedText("template.helper." + Key))
        {
            Sample = Sample,
            Freshness = Freshness,
            Scope = Scope,
        };
}
