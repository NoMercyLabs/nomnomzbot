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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>What one lookup token needs while it renders.</summary>
internal readonly record struct LookupCall(
    LookupData Data,
    LookupSources Sources,
    IReadOnlyList<string> Args,
    TemplateRenderScope Scope,
    CancellationToken Ct
)
{
    public CultureInfo Culture => NumberCulture.For(Scope.Language);

    /// <summary>The two-letter code of the channel language (<c>en</c> when unset).</summary>
    public string Language => Culture.TwoLetterISOLanguageName;

    /// <summary>The channel-language text printed when <paramref name="source"/> fails or is switched off.</summary>
    public string Fallback(string source) => LookupFallbacks.For(source, Culture);

    /// <summary>True when <paramref name="login"/> is the channel the template runs in.</summary>
    public bool IsOwnChannel(string login) =>
        string.Equals(Scope.Channel?.ChannelName, login, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One <c>lookup.*</c> root form. Its argument carries the subject and, for the Twitch, game, weather and time
/// forms, a field suffix (<c>viewertwo.game</c>, <c>Amsterdam.temp.f</c>), because the parser starts the
/// arguments at the first colon.
/// </summary>
internal sealed record LookupRow(
    string Key,
    string Sample,
    string Source,
    TemplateHelperFreshness Freshness,
    int MinArgs,
    int MaxArgs,
    Func<LookupCall, Task<string?>> Render
)
{
    public Task<string?> RenderAsync(LookupCall call) =>
        call.Args.Count < MinArgs || call.Args.Count > MaxArgs
            ? Task.FromResult<string?>(null)
            : Render(call);

    public TemplateHelperEntry ToEntry() =>
        new(Key, TemplateHelperContexts.Standard, new LocalizedText("template.helper." + Key))
        {
            Sample = Sample,
            Source = Source,
            Freshness = Freshness,
            Scope = TemplateHelperScope.Global,
        };
}

internal interface ILookupHelperGroup
{
    IReadOnlyList<LookupRow> Rows { get; }
}

internal abstract class LookupHelperGroup(params LookupRow[] rows) : ILookupHelperGroup
{
    public IReadOnlyList<LookupRow> Rows { get; } = rows;
}

/// <summary>Splits <c>subject.field</c> arguments on the longest known field suffix.</summary>
internal static class LookupArguments
{
    /// <summary>
    /// The subject and field of <paramref name="argument"/>. The field is the longest of
    /// <paramref name="fields"/> the argument ends with after a dot, or null for a plain subject.
    /// </summary>
    public static (string Subject, string? Field) Split(string argument, string[] fields)
    {
        foreach (string field in fields.OrderByDescending(f => f.Length))
        {
            if (argument.EndsWith("." + field, StringComparison.OrdinalIgnoreCase))
                return (argument[..^(field.Length + 1)].Trim(), field);
        }
        return (argument.Trim(), null);
    }
}
