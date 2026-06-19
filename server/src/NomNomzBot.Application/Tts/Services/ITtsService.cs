// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Application.Tts.Services;

public interface ITtsService
{
    Task<TtsResult> SynthesizeAsync(string text, string voiceId, CancellationToken ct = default);
    Task<IReadOnlyList<TtsVoiceInfo>> GetAvailableVoicesAsync(CancellationToken ct = default);
}

public record TtsResult(byte[] AudioData, int DurationMs, string VoiceId, string Provider);
