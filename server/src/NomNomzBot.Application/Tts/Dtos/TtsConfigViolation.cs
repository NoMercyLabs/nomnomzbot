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
