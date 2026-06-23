// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.ValueObjects;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// Maps the persisted Domain embed value object (<see cref="DiscordEmbedConfig"/>) to/from the Application wire
/// DTO (<see cref="DiscordEmbedDto"/>) — the single seam keeping the Domain free of an Application dependency.
/// </summary>
internal static class DiscordEmbedMapper
{
    public static DiscordEmbedConfig? ToDomain(DiscordEmbedDto? dto) =>
        dto is null
            ? null
            : new DiscordEmbedConfig(
                dto.Title,
                dto.Description,
                dto.Color,
                dto.ThumbnailUrl,
                dto.ImageUrl,
                dto.FooterText,
                dto.Fields is null
                    ? null
                    :
                    [
                        .. dto.Fields.Select(f => new DiscordEmbedConfigField(
                            f.Name,
                            f.Value,
                            f.Inline
                        )),
                    ]
            );

    public static DiscordEmbedDto? ToDto(DiscordEmbedConfig? config) =>
        config is null
            ? null
            : new DiscordEmbedDto(
                config.Title,
                config.Description,
                config.Color,
                config.ThumbnailUrl,
                config.ImageUrl,
                config.FooterText,
                config.Fields is null
                    ? null
                    :
                    [
                        .. config.Fields.Select(f => new DiscordEmbedFieldDto(
                            f.Name,
                            f.Value,
                            f.Inline
                        )),
                    ]
            );

    /// <summary>Renders every embed string field through the template engine against the given variables.</summary>
    public static DiscordEmbedDto RenderTemplates(
        DiscordEmbedDto embed,
        Func<string, string> render
    ) =>
        new(
            embed.Title is null ? null : render(embed.Title),
            embed.Description is null ? null : render(embed.Description),
            embed.Color,
            embed.ThumbnailUrl,
            embed.ImageUrl,
            embed.FooterText is null ? null : render(embed.FooterText),
            embed.Fields is null
                ? null
                :
                [
                    .. embed.Fields.Select(f => new DiscordEmbedFieldDto(
                        render(f.Name),
                        render(f.Value),
                        f.Inline
                    )),
                ]
        );
}
