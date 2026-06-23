// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    IReadOnlyList<DiscordEmbedConfigField>? Fields
);

/// <summary>One name/value field of a Discord embed (schema P.10).</summary>
public sealed record DiscordEmbedConfigField(string Name, string Value, bool Inline);
