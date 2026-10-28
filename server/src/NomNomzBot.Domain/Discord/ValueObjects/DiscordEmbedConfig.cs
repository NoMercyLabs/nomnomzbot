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

namespace NomNomzBot.Domain.Discord.ValueObjects;

/// <summary>
/// The persisted shape of a Discord notification embed (schema P.10 — <c>DiscordNotificationConfig.EmbedConfig</c>,
/// <c>[VC:JSON]</c>). A Domain value object so the entity carries no Application dependency; the EF Newtonsoft
/// <c>ValueConverter</c> serializes it to a single text column. The Application <c>DiscordEmbedDto</c> mirrors it
/// on the wire.
/// </summary>
public sealed record DiscordEmbedConfig(
    string? Title,
    string? Description,
    string? Color,
    string? ThumbnailUrl,
    string? ImageUrl,
    string? FooterText,
    IReadOnlyList<DiscordEmbedConfigField>? Fields,
    string? TitleUrl = null,
    string? AuthorName = null,
    string? AuthorIconUrl = null,
    string? AuthorUrl = null,
    string? FooterIconUrl = null,
    bool ShowTimestamp = false,
    string? ButtonLabel = null,
    string? ButtonUrl = null
);

/// <summary>One name/value field of a Discord embed (schema P.10).</summary>
public sealed record DiscordEmbedConfigField(string Name, string Value, bool Inline);
