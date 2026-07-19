// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// The per-channel pronunciation lexicon (tts.md) over <see cref="TtsLexiconEntry"/>. CRUD invalidates the
/// per-channel resolve-all cache; <see cref="ApplyAsync"/> is the dispatch hot path — it reads the cached
/// rule set and rewrites the utterance in ONE non-recursive pass over the original text: matches are
/// collected against the input, overlaps resolved (earliest start, then longest, then rule order), and the
/// output assembled once, so a replacement can never trigger another rule. Bounded at 200 rules with a
/// per-rule regex timeout; every phrase is regex-escaped, so no user input is ever a pattern.
/// </summary>
public class TtsLexiconService : ITtsLexiconService
{
    /// <summary>Dispatch applies at most this many rules per utterance (tts.md lexicon bound).</summary>
    internal const int MaxAppliedEntries = 200;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private readonly IApplicationDbContext _db;
    private readonly ICacheService _cache;

    public TtsLexiconService(IApplicationDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    private static string CacheKey(Guid broadcasterId) => $"tts:lexicon:{broadcasterId}";

    public async Task<Result<IReadOnlyList<TtsLexiconEntryDto>>> ListAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<TtsLexiconEntryDto> entries = await QueryChannelEntriesAsync(
            broadcasterId,
            cancellationToken
        );
        return Result.Success<IReadOnlyList<TtsLexiconEntryDto>>(entries);
    }

    public async Task<Result<TtsLexiconEntryDto>> CreateAsync(
        Guid broadcasterId,
        UpsertTtsLexiconEntryDto request,
        CancellationToken cancellationToken = default
    )
    {
        Result validated = Validate(request);
        if (validated.IsFailure)
            return Result.Failure<TtsLexiconEntryDto>(
                validated.ErrorMessage!,
                validated.ErrorCode!
            );

        string phrase = request.Phrase.Trim();
        bool duplicate = await _db.TtsLexiconEntries.AnyAsync(
            e =>
                e.BroadcasterId == broadcasterId
                && e.Phrase == phrase
                && e.MatchKind == request.MatchKind
                && e.DeletedAt == null,
            cancellationToken
        );
        if (duplicate)
            return Errors.AlreadyExists("pronunciation rule", phrase).ToTyped<TtsLexiconEntryDto>();

        TtsLexiconEntry entry = new()
        {
            BroadcasterId = broadcasterId,
            Phrase = phrase,
            Replacement = request.Replacement.Trim(),
            MatchKind = request.MatchKind,
        };
        _db.TtsLexiconEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKey(broadcasterId), cancellationToken);

        return Result.Success(ToDto(entry));
    }

    public async Task<Result<TtsLexiconEntryDto>> UpdateAsync(
        Guid broadcasterId,
        Guid entryId,
        UpsertTtsLexiconEntryDto request,
        CancellationToken cancellationToken = default
    )
    {
        Result validated = Validate(request);
        if (validated.IsFailure)
            return Result.Failure<TtsLexiconEntryDto>(
                validated.ErrorMessage!,
                validated.ErrorCode!
            );

        TtsLexiconEntry? entry = await _db.TtsLexiconEntries.FirstOrDefaultAsync(
            e => e.BroadcasterId == broadcasterId && e.Id == entryId && e.DeletedAt == null,
            cancellationToken
        );
        if (entry is null)
            return Errors.NotFound<TtsLexiconEntryDto>("pronunciation rule", entryId.ToString());

        string phrase = request.Phrase.Trim();
        bool duplicate = await _db.TtsLexiconEntries.AnyAsync(
            e =>
                e.BroadcasterId == broadcasterId
                && e.Id != entryId
                && e.Phrase == phrase
                && e.MatchKind == request.MatchKind
                && e.DeletedAt == null,
            cancellationToken
        );
        if (duplicate)
            return Errors.AlreadyExists("pronunciation rule", phrase).ToTyped<TtsLexiconEntryDto>();

        entry.Phrase = phrase;
        entry.Replacement = request.Replacement.Trim();
        entry.MatchKind = request.MatchKind;
        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKey(broadcasterId), cancellationToken);

        return Result.Success(ToDto(entry));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid entryId,
        CancellationToken cancellationToken = default
    )
    {
        TtsLexiconEntry? entry = await _db.TtsLexiconEntries.FirstOrDefaultAsync(
            e => e.BroadcasterId == broadcasterId && e.Id == entryId && e.DeletedAt == null,
            cancellationToken
        );
        if (entry is null)
            return Result.Failure($"Pronunciation rule '{entryId}' was not found.", "NOT_FOUND");

        // Remove() is converted to a soft delete (DeletedAt) by the SoftDeleteInterceptor.
        _db.TtsLexiconEntries.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKey(broadcasterId), cancellationToken);
        return Result.Success();
    }

    public async Task<string> ApplyAsync(
        Guid broadcasterId,
        string text,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(text))
            return text;

        IReadOnlyList<TtsLexiconEntryDto> entries = await GetCachedEntriesAsync(
            broadcasterId,
            cancellationToken
        );
        if (entries.Count == 0)
            return text;

        // Collect every match against the ORIGINAL text — replacements are never re-matched (no recursion).
        List<LexiconMatch> matches = [];
        int applied = Math.Min(entries.Count, MaxAppliedEntries);
        for (int order = 0; order < applied; order++)
        {
            TtsLexiconEntryDto entry = entries[order];
            string escaped = Regex.Escape(entry.Phrase);
            bool exact = entry.MatchKind == TtsLexiconMatchKinds.Exact;
            // word: lookarounds instead of \b so phrases that start/end on punctuation still bound correctly.
            string pattern = exact ? escaped : $@"(?<!\w){escaped}(?!\w)";
            RegexOptions options = RegexOptions.CultureInvariant;
            if (!exact)
                options |= RegexOptions.IgnoreCase;
            try
            {
                foreach (Match match in Regex.Matches(text, pattern, options, MatchTimeout))
                    matches.Add(
                        new LexiconMatch(match.Index, match.Length, entry.Replacement, order)
                    );
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological phrase never blocks the utterance — that one rule is simply not applied.
            }
        }
        if (matches.Count == 0)
            return text;

        // Overlap resolution: earliest start wins; ties go to the longest match, then rule order.
        matches.Sort(
            static (a, b) =>
                a.Start != b.Start ? a.Start.CompareTo(b.Start)
                : a.Length != b.Length ? b.Length.CompareTo(a.Length)
                : a.Order.CompareTo(b.Order)
        );

        StringBuilder result = new(text.Length);
        int cursor = 0;
        foreach (LexiconMatch match in matches)
        {
            if (match.Start < cursor)
                continue; // overlaps a span already replaced in this pass
            result.Append(text, cursor, match.Start - cursor).Append(match.Replacement);
            cursor = match.Start + match.Length;
        }
        result.Append(text, cursor, text.Length - cursor);
        return result.ToString();
    }

    private readonly record struct LexiconMatch(
        int Start,
        int Length,
        string Replacement,
        int Order
    );

    // The cached resolve-all for the dispatch hot path — filled from the database on miss, evicted on write.
    private async Task<IReadOnlyList<TtsLexiconEntryDto>> GetCachedEntriesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        string key = CacheKey(broadcasterId);
        List<TtsLexiconEntryDto>? cached = await _cache.GetAsync<List<TtsLexiconEntryDto>>(
            key,
            cancellationToken
        );
        if (cached is not null)
            return cached;

        List<TtsLexiconEntryDto> entries = await QueryChannelEntriesAsync(
            broadcasterId,
            cancellationToken
        );
        await _cache.SetAsync(key, entries, CacheTtl, cancellationToken);
        return entries;
    }

    private async Task<List<TtsLexiconEntryDto>> QueryChannelEntriesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    ) =>
        await _db
            .TtsLexiconEntries.Where(e => e.BroadcasterId == broadcasterId && e.DeletedAt == null)
            .OrderBy(e => e.Phrase)
            .ThenBy(e => e.MatchKind)
            .Select(e => new TtsLexiconEntryDto(e.Id, e.Phrase, e.Replacement, e.MatchKind))
            .ToListAsync(cancellationToken);

    private static Result Validate(UpsertTtsLexiconEntryDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Phrase))
            return Errors.ValidationFailed("A phrase is required.");
        if (string.IsNullOrWhiteSpace(request.Replacement))
            return Errors.ValidationFailed("A replacement is required.");
        if (!TtsLexiconMatchKinds.IsValid(request.MatchKind))
            return Errors.ValidationFailed(
                $"Unknown match kind '{request.MatchKind}' — use 'word' or 'exact'."
            );
        return Result.Success();
    }

    private static TtsLexiconEntryDto ToDto(TtsLexiconEntry entry) =>
        new(entry.Id, entry.Phrase, entry.Replacement, entry.MatchKind);
}
