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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Discord.ValueObjects;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// Gives the go-live rules saved before the ready go-live embed shipped the ready message and embed. Such a rule
/// kept its plain text and no embed, because the ready embed only starts a NEW rule. Its title and game
/// placeholders (<c>{stream.title}</c>, <c>{stream.game}</c>) read the channel's live state, which is still empty
/// when the stream starts, so the post read "Name are live! ()". The event itself carries <c>{title}</c> and
/// <c>{game}</c>, which the ready message uses. Only a <c>go_live</c> rule that still holds exactly the old
/// message and has no embed is changed: a message or an embed the streamer wrote is never touched. Order 90,
/// after the content seeders. Idempotent: the healed message no longer matches.
/// </summary>
public sealed class GoLiveEmbedHealSeeder(
    IApplicationDbContext db,
    ITemplateHelperValidator validator
) : ISeeder
{
    private const string GoLive = "go_live";
    private const string OldMessage = "{channel.name} {tense} live! {stream.title} ({stream.game})";

    public int Order => 90;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!DefaultsAreValid())
            return;

        List<DiscordNotificationConfig> rules = await db
            .DiscordNotificationConfigs.IgnoreQueryFilters()
            .Where(c =>
                c.DeletedAt == null
                && c.TriggerType == GoLive
                && c.EmbedConfig == null
                && c.MessageTemplate == OldMessage
            )
            .ToListAsync(ct);
        if (rules.Count == 0)
            return;

        foreach (DiscordNotificationConfig rule in rules)
        {
            rule.MessageTemplate = DiscordConfigDefaults.GoLiveMessage;
            rule.EmbedConfig = DiscordConfigDefaults.GoLiveEmbed;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The same save-time check a streamer's own text passes: every placeholder of the ready message and embed must
    /// be a helper the Discord post fills. A default that fails it is never written.
    /// </summary>
    private bool DefaultsAreValid()
    {
        DiscordEmbedConfig embed = DiscordConfigDefaults.GoLiveEmbed;
        IEnumerable<string?> texts =
        [
            DiscordConfigDefaults.GoLiveMessage,
            embed.Title,
            embed.TitleUrl,
            embed.AuthorName,
            embed.AuthorIconUrl,
            embed.ImageUrl,
            .. embed.Fields?.Select(f => f.Value) ?? [],
        ];

        return texts.All(t => validator.Validate(t, TemplateHelperContext.Discord).IsSuccess);
    }
}
