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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// See <see cref="IReadAloudService"/>. English speaks in the platform default voice (the bot's own voice). Any
/// other language speaks in the first Edge catalogue voice of that language, preferring the main country of the
/// language (nl-NL for nl), then by id, so the same page always sounds the same.
/// </summary>
public sealed class ReadAloudService(
    IApplicationDbContext db,
    ITtsService tts,
    IPlatformTtsVoiceDefault defaultVoice
) : IReadAloudService
{
    /// <summary>The longest text one call speaks: a long sentence, never a page.</summary>
    public const int MaxTextLength = 500;

    /// <summary>
    /// How many clips the public voice makes at the same moment, across every caller. The voice provider gave
    /// an empty clip to some requests that ran in parallel, and the route is open to anyone, so a few at a time
    /// bounds the cost of a flood. The rest wait for a slot; a clip already cached never reaches this.
    /// </summary>
    public const int MaxSynthesesAtOnce = 2;

    private const string EdgeProvider = "edge";

    private static readonly SemaphoreSlim Slots = new(MaxSynthesesAtOnce);
    private static readonly TimeSpan WaitForSlot = TimeSpan.FromSeconds(30);

    public async Task<Result<TtsResult>> SynthesizeAsync(
        string text,
        string locale,
        CancellationToken ct = default
    )
    {
        string spoken = text.Trim();
        if (spoken.Length == 0 || spoken.Length > MaxTextLength)
            return Result.Failure<TtsResult>(
                $"Read-aloud text must be 1 to {MaxTextLength} characters.",
                "VALIDATION_FAILED"
            );

        string voiceId = await ResolveVoiceIdAsync(locale, ct);
        if (!await Slots.WaitAsync(WaitForSlot, ct))
            return Unavailable();

        try
        {
            TtsResult clip = await tts.SynthesizeAsync(spoken, voiceId, ct: ct);

            // An empty clip is a failure: it cannot be played, and a cache would keep it.
            return clip.AudioData.Length > 0 ? Result.Success(clip) : Unavailable();
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or IOException
                        or InvalidOperationException
                        or TimeoutException
            )
        {
            return Unavailable();
        }
        finally
        {
            Slots.Release();
        }
    }

    private static Result<TtsResult> Unavailable() =>
        Result.Failure<TtsResult>("The voice is not available right now.", "TTS_UNAVAILABLE");

    private async Task<string> ResolveVoiceIdAsync(string locale, CancellationToken ct)
    {
        string language = locale.Split('-', '_')[0].ToLowerInvariant();
        if (language is "" or "en")
            return await defaultVoice.GetVoiceIdAsync(ct);

        string mainLocale = $"{language}-{language.ToUpperInvariant()}";
        string? voiceId = await db
            .TtsVoices.Where(v => v.Provider == EdgeProvider && v.Locale.StartsWith(language + "-"))
            .OrderBy(v => v.Locale == mainLocale ? 0 : 1)
            .ThenBy(v => v.Id)
            .Select(v => v.Id)
            .FirstOrDefaultAsync(ct);

        return voiceId ?? await defaultVoice.GetVoiceIdAsync(ct);
    }
}
