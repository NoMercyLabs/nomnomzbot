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

using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Infrastructure.Identity.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Tts.Pipeline;

/// <summary>
/// Supplies who a <c>play_tts</c> line can be read as, for the <c>tts_speaker</c> picker kind: the closed words the
/// action understands (<c>bot</c>, <c>viewer</c>, <c>user</c>, <c>broadcaster</c>) followed by the channel's known
/// viewers, which are exactly the <c>twitch_user</c> list (the saved value is the Twitch user id, a literal platform
/// user id as the action reads it). The words that match the search come first on the first page and are counted in
/// the total. A saved word finds the word; any other saved value is looked up as a viewer.
/// </summary>
internal sealed class TtsSpeakerOptionProvider : IPipelineOptionProvider
{
    private static readonly (string Word, string Meaning)[] Words =
    [
        ("bot", "The bot's own line, in the channel's voice"),
        ("viewer", "The viewer who triggered it, in their voice"),
        ("user", "The viewer who triggered it, in their voice"),
        ("broadcaster", "The channel owner, in their voice"),
    ];

    private readonly TwitchUserOptionProvider _viewers;

    public TtsSpeakerOptionProvider(IApplicationDbContext db)
    {
        _viewers = new(db);
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.TtsSpeaker;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        List<PipelineOption> words = WordsFor(request);
        if (request.IsValueLookup && words.Count > 0)
            return Result.Success(PipelineOptionListResult.Of(words, words.Count));

        Result<PipelineOptionListResult> viewers = await _viewers.GetOptionsAsync(
            broadcasterId,
            request,
            ct
        );
        if (viewers.IsFailure || !viewers.Value.SourceAvailable)
            return viewers;

        List<PipelineOption> first = request.Pagination.Page == 1 ? words : [];
        return Result.Success(
            PipelineOptionListResult.Of(
                [.. first, .. viewers.Value.Items],
                viewers.Value.TotalCount + words.Count
            )
        );
    }

    private static List<PipelineOption> WordsFor(PipelineOptionQuery request) =>
        [
            .. Words
                .Where(w => WordMatches(w.Word, w.Meaning, request))
                .Select(w => PipelineOptionPage.Option(w.Word, w.Word, w.Meaning)),
        ];

    private static bool WordMatches(string word, string meaning, PipelineOptionQuery request)
    {
        if (request.IsValueLookup)
            return word == request.Value;

        return string.IsNullOrWhiteSpace(request.Search)
            || word.Contains(request.Search, StringComparison.OrdinalIgnoreCase)
            || meaning.Contains(request.Search, StringComparison.OrdinalIgnoreCase);
    }
}
