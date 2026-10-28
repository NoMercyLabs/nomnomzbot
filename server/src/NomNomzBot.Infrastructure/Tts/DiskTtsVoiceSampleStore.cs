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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Keeps each voice's introduction clip on disk under <c>tts-samples/</c>: the audio in one file and the small
/// facts about it (provider, length) in a second file beside it. The file name is a hash of the voice id, so no
/// voice id can name a path outside the folder.
/// </summary>
public sealed class DiskTtsVoiceSampleStore : ITtsVoiceSampleStore
{
    private readonly string _directory;

    public DiskTtsVoiceSampleStore()
        : this(SelfHostDataPaths.TtsSamplesDirectory) { }

    public DiskTtsVoiceSampleStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public async Task<TtsResult?> GetAsync(
        string voiceId,
        CancellationToken cancellationToken = default
    )
    {
        string stem = StemOf(voiceId);
        string audioFile = Path.Combine(_directory, stem + ".mp3");
        string factsFile = Path.Combine(_directory, stem + ".json");
        if (!File.Exists(audioFile) || !File.Exists(factsFile))
            return null;

        try
        {
            byte[] audio = await File.ReadAllBytesAsync(audioFile, cancellationToken);
            await using FileStream facts = File.OpenRead(factsFile);
            SampleFacts? read = await JsonSerializer.DeserializeAsync<SampleFacts>(
                facts,
                cancellationToken: cancellationToken
            );
            return read is null || audio.Length == 0
                ? null
                : new TtsResult(audio, read.DurationMs, read.VoiceId, read.Provider);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        string voiceId,
        TtsResult clip,
        CancellationToken cancellationToken = default
    )
    {
        string stem = StemOf(voiceId);
        SampleFacts facts = new(clip.VoiceId, clip.Provider, clip.DurationMs);

        // The facts file is written last: a reader finds a clip only when both files are there.
        await File.WriteAllBytesAsync(
            Path.Combine(_directory, stem + ".mp3"),
            clip.AudioData,
            cancellationToken
        );
        await File.WriteAllTextAsync(
            Path.Combine(_directory, stem + ".json"),
            JsonSerializer.Serialize(facts),
            cancellationToken
        );
    }

    private static string StemOf(string voiceId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(voiceId.ToLowerInvariant())));

    private sealed record SampleFacts(string VoiceId, string Provider, int DurationMs);
}
