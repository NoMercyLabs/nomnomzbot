// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;

namespace NomNomzBot.Application.Tts.Services;

/// <summary>
/// The per-channel TTS pronunciation lexicon: CRUD over the rules plus the dispatch hot-path substitution.
/// The resolve-all read is cached per channel and invalidated on every write, so <see cref="ApplyAsync"/>
/// stays off the database for the common case.
/// </summary>
public interface ITtsLexiconService
{
    /// <summary>All active lexicon rules for the channel, ordered by phrase.</summary>
    Task<Result<IReadOnlyList<TtsLexiconEntryDto>>> ListAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Add a rule; a duplicate (phrase, match kind) on the channel is refused, not doubled.</summary>
    Task<Result<TtsLexiconEntryDto>> CreateAsync(
        Guid broadcasterId,
        UpsertTtsLexiconEntryDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Rewrite an existing rule (phrase, replacement, and match kind).</summary>
    Task<Result<TtsLexiconEntryDto>> UpdateAsync(
        Guid broadcasterId,
        Guid entryId,
        UpsertTtsLexiconEntryDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Soft-delete a rule; its (phrase, match kind) becomes free to re-add.</summary>
    Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid entryId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Apply the channel's lexicon to <paramref name="text"/> — one bounded, non-recursive pass over the
    /// original text (at most 200 rules; replacements are never re-matched). Infallible by design: any
    /// per-rule failure degrades to leaving that rule unapplied, never to blocking the utterance.
    /// </summary>
    Task<string> ApplyAsync(
        Guid broadcasterId,
        string text,
        CancellationToken cancellationToken = default
    );
}
