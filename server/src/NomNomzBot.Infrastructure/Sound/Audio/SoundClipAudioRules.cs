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

namespace NomNomzBot.Infrastructure.Sound.Audio;

/// <summary>
/// The one set of file rules for sound clip audio: a channel's own clip upload and a platform audio file
/// upload both run it, so a file the platform accepts is a file every channel accepts on install. Format is
/// content-sniffed (mp3 / ogg / wav), size and duration are fixed safety baselines, never tier-scaled.
/// </summary>
internal static class SoundClipAudioRules
{
    /// <summary>10 MB per file (sound-system spec D4 safe baseline).</summary>
    public const int MaxSizeBytes = 10 * 1024 * 1024;

    /// <summary>3 minutes per file — a sound clip is an effect or a jingle, not a track.</summary>
    public const int MaxDurationMs = 3 * 60 * 1000;

    private const int SniffLength = 12;

    /// <summary>
    /// Reads <paramref name="content"/> to the end and checks format, size and duration. On success the
    /// returned buffer holds the whole file, positioned at 0.
    /// </summary>
    public static async Task<Result<ValidatedAudio>> ValidateAsync(
        System.IO.Stream content,
        CancellationToken ct
    )
    {
        MemoryStream buffer = new();
        await content.CopyToAsync(buffer, ct);

        Result<ValidatedAudio> checkedAudio = Check(buffer);
        if (checkedAudio.IsFailure)
            await buffer.DisposeAsync();
        return checkedAudio;
    }

    private static Result<ValidatedAudio> Check(MemoryStream buffer)
    {
        if (buffer.Length < 4)
            return Result.Failure<ValidatedAudio>(
                "Upload too small to be a valid audio file.",
                "INVALID_FORMAT"
            );

        byte[] header = new byte[Math.Min(SniffLength, (int)buffer.Length)];
        buffer.Position = 0;
        buffer.ReadExactly(header, 0, header.Length);
        string? mimeType = AudioSniffer.Sniff(header);
        if (mimeType is null)
            return Result.Failure<ValidatedAudio>(
                "File content is not a supported audio format (mp3, ogg, wav).",
                "INVALID_FORMAT"
            );

        if (buffer.Length > MaxSizeBytes)
            return Result.Failure<ValidatedAudio>(
                $"Clip exceeds the {MaxSizeBytes / 1024 / 1024} MB size limit.",
                "SIZE_EXCEEDED"
            );

        int durationMs = AudioSniffer.ProbeDurationMs(buffer, mimeType);
        if (durationMs > MaxDurationMs)
            return Result.Failure<ValidatedAudio>(
                $"Clip is longer than the {MaxDurationMs / 60_000} minute limit.",
                "DURATION_EXCEEDED"
            );

        buffer.Position = 0;
        return Result.Success(new ValidatedAudio(buffer, mimeType, durationMs));
    }
}

/// <summary>A file that passed <see cref="SoundClipAudioRules"/>: the bytes, the sniffed type and the probed length.</summary>
internal sealed record ValidatedAudio(MemoryStream Content, string MimeType, int DurationMs)
    : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
