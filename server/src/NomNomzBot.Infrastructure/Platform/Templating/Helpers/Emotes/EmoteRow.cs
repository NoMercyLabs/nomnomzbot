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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>What a render of one emote token needs.</summary>
internal readonly record struct EmoteCall(
    EmoteData Data,
    IReadOnlyList<string> Args,
    TemplateRenderScope Scope,
    Random Random
)
{
    public CultureInfo Culture => NumberCulture.For(Scope.Language);

    /// <summary>One random item of <paramref name="pool"/>; the injected random is shared, so a draw is locked.</summary>
    public T Pick<T>(IReadOnlyList<T> pool)
    {
        lock (Random)
            return pool[Random.Next(pool.Count)];
    }
}

internal sealed record EmoteRow(
    string Key,
    string Sample,
    EmoteSources Sources,
    int MinArgs,
    int MaxArgs,
    Func<EmoteCall, Task<string?>> Render
)
{
    /// <summary>Whose data the row reads (the viewer who typed the command, or the channel).</summary>
    public TemplateHelperScope Scope { get; init; } = TemplateHelperScope.Channel;

    public TemplateHelperFreshness Freshness =>
        Sources == EmoteSources.None
            ? TemplateHelperFreshness.Pure
            : TemplateHelperFreshness.Cached;

    /// <summary>A row over a pool of emotes: it prints what <paramref name="print"/> makes of the pool.</summary>
    public static EmoteRow Over(
        string key,
        string sample,
        EmoteSources sources,
        Func<EmoteData, Task<IReadOnlyList<EmoteItem>>> pool,
        Func<IReadOnlyList<EmoteItem>, EmoteCall, string> print
    ) => new(key, sample, sources, 0, 0, async call => print(await pool(call.Data), call));

    /// <summary>A row that reads its sources itself.</summary>
    public static EmoteRow Custom(
        string key,
        string sample,
        EmoteSources sources,
        Func<EmoteCall, Task<string?>> render,
        int minArgs = 0,
        int maxArgs = 0
    ) => new(key, sample, sources, minArgs, maxArgs, render);

    public EmoteRow ForViewer() => this with { Scope = TemplateHelperScope.Viewer };

    public Task<string?> RenderAsync(EmoteCall call) =>
        call.Args.Count < MinArgs || call.Args.Count > MaxArgs
            ? Task.FromResult<string?>(null)
            : Render(call);

    public TemplateHelperEntry ToEntry() =>
        new(Key, TemplateHelperContexts.Standard, new LocalizedText("template.helper." + Key))
        {
            Sample = Sample,
            Source = Sources == EmoteSources.None ? null : Sources.ToString().ToLowerInvariant(),
            Freshness = Freshness,
            Scope = Scope,
        };
}
