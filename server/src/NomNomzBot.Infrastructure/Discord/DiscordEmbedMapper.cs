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
                    ],
                dto.TitleUrl,
                dto.AuthorName,
                dto.AuthorIconUrl,
                dto.AuthorUrl,
                dto.FooterIconUrl,
                dto.ShowTimestamp,
                dto.ButtonLabel,
                dto.ButtonUrl
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
                    ],
                config.TitleUrl,
                config.AuthorName,
                config.AuthorIconUrl,
                config.AuthorUrl,
                config.FooterIconUrl,
                config.ShowTimestamp,
                config.ButtonLabel,
                config.ButtonUrl
            );

    /// <summary>Renders every embed string field through <see cref="Application.Abstractions.Templating.ITemplateResolver"/>
    /// (the SAME engine preview and dispatch both use) against the given variables.</summary>
    public static async Task<DiscordEmbedDto> RenderTemplatesAsync(
        DiscordEmbedDto embed,
        Func<string, Task<string>> render
    )
    {
        string? title = embed.Title is null ? null : await render(embed.Title);
        string? description = embed.Description is null ? null : await render(embed.Description);
        string? footerText = embed.FooterText is null ? null : await render(embed.FooterText);
        string? thumbnailUrl = await RenderLinkAsync(embed.ThumbnailUrl, render);
        string? imageUrl = await RenderLinkAsync(embed.ImageUrl, render);

        List<DiscordEmbedFieldDto>? fields = null;
        if (embed.Fields is not null)
        {
            fields = [];
            foreach (DiscordEmbedFieldDto f in embed.Fields)
                fields.Add(new(await render(f.Name), await render(f.Value), f.Inline));
        }

        return new(
            title,
            description,
            embed.Color,
            thumbnailUrl,
            imageUrl,
            footerText,
            fields,
            await RenderLinkAsync(embed.TitleUrl, render),
            await RenderTextAsync(embed.AuthorName, render),
            await RenderLinkAsync(embed.AuthorIconUrl, render),
            await RenderLinkAsync(embed.AuthorUrl, render),
            await RenderLinkAsync(embed.FooterIconUrl, render),
            embed.ShowTimestamp,
            await RenderTextAsync(embed.ButtonLabel, render),
            await RenderLinkAsync(embed.ButtonUrl, render)
        );
    }

    private static async Task<string?> RenderTextAsync(
        string? template,
        Func<string, Task<string>> render
    )
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        string rendered = (await render(template)).Trim();
        return rendered.Length == 0 ? null : rendered;
    }

    /// <summary>
    /// Renders a link or picture field. A field that renders to nothing, or to anything but a web address,
    /// becomes null so it leaves the payload: Discord refuses an embed whose picture or link address is
    /// empty or is not an http or https address.
    /// </summary>
    private static async Task<string?> RenderLinkAsync(
        string? template,
        Func<string, Task<string>> render
    )
    {
        string? rendered = await RenderTextAsync(template, render);

        return
            rendered is not null
            && Uri.TryCreate(rendered, UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "http" or "https"
            ? rendered
            : null;
    }
}
