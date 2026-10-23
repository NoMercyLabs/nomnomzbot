// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

    private const string EdgeProvider = "edge";

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
        try
        {
            return Result.Success(await tts.SynthesizeAsync(spoken, voiceId, ct: ct));
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or IOException
                        or InvalidOperationException
                        or TimeoutException
            )
        {
            return Result.Failure<TtsResult>(
                "The voice is not available right now.",
                "TTS_UNAVAILABLE"
            );
        }
    }

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
