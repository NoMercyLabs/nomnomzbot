// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// S-TWO-TEMPLATE-ENGINES: proves Discord notifications render through the SAME
/// <see cref="ITemplateResolver"/> every other template surface uses — never a second, weaker engine
/// that can silently diverge from what the streamer previewed.
/// </summary>
public sealed class DiscordTemplateEngineUnificationTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 22, 18, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Template_WithHelperAndTransform_RendersIdenticallyOnAnotherSurface()
    {
        // A HELPER ({botname}) wrapped in a {transform.*} transform — exactly the S042 grammar every
        // other template surface (command/event-response/timer/pipeline) uses.
        const string template = "{transform.upper:{botname}}";
        ITemplateResolver resolver = DiscordTemplateTestSupport.CreateResolver();

        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        await DiscordTestHarness.SeedConfigAsync(
            database,
            channel,
            connectionId,
            template,
            "discord-chan-1"
        );
        RecordingGateway gateway = new();

        await using DiscordTestDbContext db = database.NewContext();
        DiscordGuildService guildService = new(
            db,
            new RecordingVault(),
            new DiscordTestUnitOfWork(db),
            new RecordingEventBus(),
            Clock
        );
        DiscordNotificationDispatcher dispatcher = new(
            db,
            guildService,
            gateway,
            resolver,
            new RecordingEventBus(),
            Clock
        );

        Result<DiscordDispatchOutcomeDto> result = await dispatcher.DispatchAsync(
            new(channel, "go_live", "go_live:s1", null, new Dictionary<string, string>())
        );
        result.Value.Status.Should().Be("sent");
        string dispatchedContent = gateway.Posts.Single().Message.Content;

        // "Another surface": the SAME template string, rendered directly through ITemplateResolver the
        // way a command/event-response/pipeline template would be — with matching inputs (no seed
        // variables, same broadcaster, same clock), the two renders MUST be byte-identical because
        // there is only one engine now.
        string directRender = await resolver.ResolveAsync(
            template,
            new Dictionary<string, string>(),
            channel
        );

        dispatchedContent.Should().Be(directRender);
        dispatchedContent.Should().NotContain("{"); // the helper + transform both actually resolved
    }

    [Fact]
    public async Task Preview_AndDispatch_OfTheSameRule_ProduceTheSameRenderedText()
    {
        const string template = "{channel.name} playing {channel.game}";

        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);

        Guid configId;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordNotificationConfigService configService = new(
                db,
                new DiscordTestUnitOfWork(db),
                DiscordTemplateTestSupport.CreateResolver(),
                DiscordTemplateTestSupport.CreateValidator()
            );
            Result<DiscordNotificationConfigDto> created = await configService.CreateConfigAsync(
                channel,
                connectionId,
                new("go_live", true, "discord-chan-1", null, template, null, null, null)
            );
            created.IsSuccess.Should().BeTrue(created.ErrorMessage);
            configId = created.Value.Id;
        }

        // 1. PREVIEW path — the streamer's "what will this look like" view. The service's own sample
        // data supplies channel.name = "SampleStreamer" and channel.game = "Just Chatting" (discord.md §3.2).
        string previewContent;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordNotificationConfigService configService = new(
                db,
                new DiscordTestUnitOfWork(db),
                DiscordTemplateTestSupport.CreateResolver(),
                DiscordTemplateTestSupport.CreateValidator()
            );
            Result<DiscordNotificationPreviewDto> preview = await configService.PreviewAsync(
                channel,
                configId
            );
            preview.IsSuccess.Should().BeTrue(preview.ErrorMessage);
            previewContent = preview.Value.RenderedContent;
        }

        // 2. DISPATCH path — the actual post, fed the SAME two variables a real go-live event would
        // carry for a streamer named "SampleStreamer" playing "Just Chatting".
        RecordingGateway gateway = new();
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordGuildService guildService = new(
                db,
                new RecordingVault(),
                new DiscordTestUnitOfWork(db),
                new RecordingEventBus(),
                Clock
            );
            DiscordNotificationDispatcher dispatcher = new(
                db,
                guildService,
                gateway,
                DiscordTemplateTestSupport.CreateResolver(),
                new RecordingEventBus(),
                Clock
            );
            Result<DiscordDispatchOutcomeDto> dispatched = await dispatcher.DispatchAsync(
                new(
                    channel,
                    "go_live",
                    "go_live:s1",
                    null,
                    new Dictionary<string, string>
                    {
                        ["channel.name"] = "SampleStreamer",
                        ["channel.game"] = "Just Chatting",
                    }
                )
            );
            dispatched.Value.Status.Should().Be("sent");
        }

        string dispatchedContent = gateway.Posts.Single().Message.Content;

        // Same rule, same effective inputs, SAME engine on both paths — the preview never lies about
        // what actually gets posted.
        previewContent.Should().Be("SampleStreamer playing Just Chatting");
        dispatchedContent.Should().Be(previewContent);
    }
}
