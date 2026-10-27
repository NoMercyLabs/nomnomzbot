// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
