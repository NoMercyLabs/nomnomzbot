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

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>The data of a <c>bot.shoutout.sent</c> activity row: who was shouted out, how, and the text and
/// speech viewers got. Rows from before the text was kept carry only the first two fields.</summary>
public sealed record ShoutoutSentRecord(
    [property: JsonPropertyName("toUserId")] string ToUserId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("announcement")] string? Announcement,
    [property: JsonPropertyName("toDisplayName")] string? ToDisplayName,
    [property: JsonPropertyName("toLogin")] string? ToLogin,
    [property: JsonPropertyName("spoke")] bool Spoke
);
