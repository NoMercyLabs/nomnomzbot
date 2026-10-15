// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
