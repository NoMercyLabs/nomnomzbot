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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Stream.Pipeline;

/// <summary>
/// Supplies the <c>stream_language</c> picker: every broadcaster language Twitch offers, named in English with
/// its own name beside it, and the code as the saved value. A search matches the code or either name; a saved
/// value is answered with that one language.
/// </summary>
internal sealed class StreamLanguageOptionProvider : IPipelineOptionProvider
{
    public PipelineActionFieldKind Kind => PipelineActionFieldKind.StreamLanguage;

    public Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        List<PipelineOption> options =
        [
            .. TwitchStreamLanguages.All.Select(Option).Where(option => Matches(option, request)),
        ];
        return Task.FromResult(Result.Success(PipelineOptionListResult.Of(options, options.Count)));
    }

    private static PipelineOption Option(string code) =>
        new(
            code,
            TwitchStreamLanguages.EnglishName(code),
            TwitchStreamLanguages.NativeName(code),
            ImageUrl: null,
            PipelineOptionState.Selectable
        );

    private static bool Matches(PipelineOption option, PipelineOptionQuery request)
    {
        if (request.IsValueLookup)
            return string.Equals(option.Value, request.Value, StringComparison.OrdinalIgnoreCase);

        string term = request.Search?.Trim() ?? string.Empty;
        return term.Length == 0
            || option.Value.Contains(term, StringComparison.OrdinalIgnoreCase)
            || option.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (option.SecondaryText?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
