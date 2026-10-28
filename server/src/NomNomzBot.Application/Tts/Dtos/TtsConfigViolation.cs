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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Tts.Dtos;

/// <summary>
/// Why a TTS config save was refused, as data the dashboard translates itself: <see cref="Key"/> is the
/// i18n key of the message, <see cref="Field"/> the request field to mark (camelCase, as in the JSON body).
/// </summary>
public sealed record TtsConfigViolation(string Key, string Field) : IResponseErrorData
{
    public const string CooldownOutOfRange = "tts.config.error.cooldownOutOfRange";
    public const string MaxCharactersOutOfRange = "tts.config.error.maxCharactersOutOfRange";
    public const string MinBitsOutOfRange = "tts.config.error.minBitsOutOfRange";
}
