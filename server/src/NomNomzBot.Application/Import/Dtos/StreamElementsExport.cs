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

namespace NomNomzBot.Application.Import.Dtos;

/// <summary>
/// A tolerant view of a StreamElements chatbot export (the JSON a streamer downloads from their SE bot
/// dashboard). Every field is nullable and every collection optional so a partial or older export never fails
/// to bind — missing sections simply import nothing. Only the commands / quotes / timers surfaces are read;
/// overlays and widgets are out of scope (a future import).
/// </summary>
public sealed record StreamElementsExport
{
    [JsonPropertyName("commands")]
    public List<SeCommand>? Commands { get; init; }

    [JsonPropertyName("quotes")]
    public List<SeQuote>? Quotes { get; init; }

    [JsonPropertyName("timers")]
    public List<SeTimer>? Timers { get; init; }
}

/// <summary>
/// A StreamElements custom command. <c>AccessLevel</c> is SE's numeric permission ladder
/// (0=everyone … 100=subscriber … 500=broadcaster), mapped to this project's role ladder on import.
/// SE separates a global <c>Cooldown</c> from a per-user <c>UserCooldown</c> (both in seconds).
/// </summary>
public sealed record SeCommand
{
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("response")]
    public string? Response { get; init; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("cooldown")]
    public int? Cooldown { get; init; }

    [JsonPropertyName("userCooldown")]
    public int? UserCooldown { get; init; }

    [JsonPropertyName("accessLevel")]
    public int? AccessLevel { get; init; }

    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; init; }
}

/// <summary>A StreamElements quote. <c>AddedBy</c> is the attributed author; <c>Game</c> the context category.</summary>
public sealed record SeQuote
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("addedBy")]
    public string? AddedBy { get; init; }

    [JsonPropertyName("game")]
    public string? Game { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; init; }
}

/// <summary>
/// A StreamElements timer. SE stores the recurrence as an <c>Interval</c> in SECONDS and a minimum
/// <c>ChatLines</c> activity gate. A timer may carry either a single <c>Message</c> or a <c>Messages</c> list;
/// both are accepted and merged on import.
/// </summary>
public sealed record SeTimer
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("messages")]
    public List<string>? Messages { get; init; }

    [JsonPropertyName("interval")]
    public int? Interval { get; init; }

    [JsonPropertyName("chatLines")]
    public int? ChatLines { get; init; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }
}
