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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>One helper key of the <c>ad</c>, <c>schedule</c> or <c>activity</c> namespace and how it renders.</summary>
internal sealed record BroadcastRow<TData>(
    string Key,
    string Sample,
    string Source,
    TemplateHelperFreshness Freshness,
    int MinArgs,
    int MaxArgs,
    Func<BroadcastCall<TData>, Task<string?>> Render
)
{
    /// <summary>A row with no argument. A null result prints the token as written.</summary>
    public static BroadcastRow<TData> Of(
        string key,
        string sample,
        string source,
        TemplateHelperFreshness freshness,
        Func<BroadcastCall<TData>, Task<string?>> render
    ) => new(key, sample, source, freshness, 0, 0, render);

    /// <summary>A row with a fixed number of arguments (the call holds exactly that many).</summary>
    public static BroadcastRow<TData> WithArgs(
        string key,
        string sample,
        string source,
        TemplateHelperFreshness freshness,
        int args,
        Func<BroadcastCall<TData>, Task<string?>> render
    ) => new(key, sample, source, freshness, args, args, render);

    public Task<string?> RenderAsync(BroadcastCall<TData> call) =>
        call.Args.Count < MinArgs || call.Args.Count > MaxArgs
            ? Task.FromResult<string?>(null)
            : Render(call);

    public TemplateHelperEntry ToEntry() =>
        new(
            Key,
            [.. Enum.GetValues<TemplateHelperContext>()],
            new LocalizedText("template.helper." + Key)
        )
        {
            Sample = Sample,
            Source = Source,
            Freshness = Freshness,
        };
}
