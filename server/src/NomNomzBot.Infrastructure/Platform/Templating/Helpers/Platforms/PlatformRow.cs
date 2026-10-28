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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

internal readonly record struct PlatformCall(
    PlatformData Data,
    IReadOnlyList<string> Args,
    TemplateRenderScope Scope
)
{
    public CultureInfo Culture => NumberCulture.For(Scope.Language);
}

/// <summary>
/// One <c>platform.*</c> helper form. A form prints an empty text for a platform that is not connected (or not
/// live), never a 0 as if it were; null (the token as written) is for a platform name that is not known.
/// </summary>
internal sealed record PlatformRow(
    string Key,
    string Sample,
    PlatformSources Sources,
    int MinArgs,
    int MaxArgs,
    Func<PlatformCall, Task<string?>> Render
)
{
    public TemplateHelperFreshness Freshness =>
        Sources == PlatformSources.None
            ? TemplateHelperFreshness.Seed
            : TemplateHelperFreshness.Cached;

    /// <summary>A form that reads a seed variable only.</summary>
    public static PlatformRow Seed(string key, string sample, Func<PlatformData, string?> print) =>
        new(key, sample, PlatformSources.None, 0, 0, call => Task.FromResult(print(call.Data)));

    /// <summary>A form without arguments that reads its sources itself.</summary>
    public static PlatformRow Custom(
        string key,
        string sample,
        PlatformSources sources,
        Func<PlatformCall, Task<string?>> render
    ) => new(key, sample, sources, 0, 0, render);

    /// <summary>A form with one argument: the platform name or key. An unknown platform prints the token as written.</summary>
    public static PlatformRow OfPlatform(
        string key,
        string sample,
        PlatformSources sources,
        Func<PlatformCall, PlatformNames.Info, Task<string?>> render
    ) =>
        new(
            key,
            sample,
            sources,
            1,
            1,
            call =>
                PlatformNames.Find(call.Args[0]) is { } platform
                    ? render(call, platform)
                    : Task.FromResult<string?>(null)
        );

    public Task<string?> RenderAsync(PlatformCall call) =>
        call.Args.Count < MinArgs || call.Args.Count > MaxArgs
            ? Task.FromResult<string?>(null)
            : Render(call);

    public TemplateHelperEntry ToEntry() =>
        new(Key, TemplateHelperContexts.Standard, new LocalizedText("template.helper." + Key))
        {
            Sample = Sample,
            Source = Sources == PlatformSources.None ? null : Sources.ToString().ToLowerInvariant(),
            Freshness = Freshness,
        };
}
