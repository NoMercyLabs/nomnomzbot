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

using System.Text.RegularExpressions;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// <c>lookup.translate</c> and <c>lookup.translate.chat</c>: MyMemory translation. With two arguments the
/// source language is English; <c>translate.chat</c> reads the command text (the <c>args</c> seed, else the
/// <c>message</c> seed) and takes the channel language as its source, because MyMemory documents no
/// autodetect.
/// </summary>
internal sealed partial class LookupTranslateHelpers()
    : LookupHelperGroup(
        new LookupRow(
            "lookup.translate",
            "goedemorgen",
            "translate",
            TemplateHelperFreshness.Cached,
            2,
            3,
            TranslateAsync
        ),
        new LookupRow(
            "lookup.translate.chat",
            "good morning",
            "translate",
            TemplateHelperFreshness.Cached,
            1,
            1,
            ChatAsync
        )
    )
{
    private static readonly string[] TriggerSeeds = ["args", "message"];

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z]{2,4})?$")]
    private static partial Regex LanguagePattern();

    private static Task<string?> TranslateAsync(LookupCall call) =>
        call.Args.Count == 2
            ? Translate(call, call.Args[1], "en", call.Args[0])
            : Translate(call, call.Args[2], call.Args[0], call.Args[1]);

    private static Task<string?> ChatAsync(LookupCall call)
    {
        string? text = TriggerSeeds
            .Select(seed => call.Scope.Variables.GetValueOrDefault(seed))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return text is null
            ? Task.FromResult<string?>(null)
            : Translate(call, text.Trim(), call.Language, call.Args[0]);
    }

    private static async Task<string?> Translate(
        LookupCall call,
        string text,
        string from,
        string to
    )
    {
        if (text.Length == 0 || !LanguagePattern().IsMatch(from) || !LanguagePattern().IsMatch(to))
            return null;

        from = from.ToLowerInvariant();
        to = to.ToLowerInvariant();
        if (from == to)
            return text;

        return await call.Sources.Translation.TranslateAsync(text, from, to, call.Ct)
            ?? call.Fallback(LookupFallbacks.Translate);
    }
}
