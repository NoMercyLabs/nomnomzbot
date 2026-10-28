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

using System.Text.Json.Serialization;

namespace NomNomzBot.Domain.Moderation.Enums;

/// <summary>
/// How a <c>ChatFilter</c> (moderation.md J.6) matches a chat message: a compiled <see cref="Regex"/>
/// pattern, a <see cref="Blocklist"/> of literal terms, or a <see cref="LinkPolicy"/> (URL detection).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatFilterType
{
    Regex,
    Blocklist,
    LinkPolicy,
}
