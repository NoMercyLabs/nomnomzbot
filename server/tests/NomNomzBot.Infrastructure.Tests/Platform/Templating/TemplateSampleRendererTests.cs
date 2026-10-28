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

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// The variable picker's "Becomes ..." examples show the signed-in user and the current channel, produced
/// by the same engine that renders a real template, instead of a fixed made-up pair.
/// </summary>
public sealed class TemplateSampleRendererTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000f001");

    private static async Task<Dictionary<string, string>> SamplesAsync(
        string channelLogin,
        string channelDisplay,
        string channelTwitchId,
        User signedIn
    )
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        db.Users.Add(signedIn);
        await db.SaveChangesAsync();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry
            .Get(Channel)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Channel,
                    TwitchChannelId = channelTwitchId,
                    ChannelName = channelLogin,
                    DisplayName = channelDisplay,
                }
            );

        TemplateResolver resolver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
        TemplateSampleRenderer renderer = new(db, resolver);

        IReadOnlyList<TemplateHelperEntry> personalized = await renderer.PersonalizeAsync(
            TemplateHelperRegistry.ForContext(TemplateHelperContext.Command),
            Channel,
            signedIn.Id
        );

        return personalized.ToDictionary(e => e.Key, e => e.Sample);
    }

    private static User Viewer(string login, string display, string twitchId) =>
        new()
        {
            Username = login,
            UsernameNormalized = login.ToLowerInvariant(),
            DisplayName = display,
            TwitchUserId = twitchId,
        };

    [Fact]
    public async Task Channel_examples_show_the_current_channel_and_user_examples_show_the_requester()
    {
        Dictionary<string, string> samples = await SamplesAsync(
            "sunny_streams",
            "Sunny_Streams",
            "555001",
            Viewer("moss_mod", "Moss_Mod", "777002")
        );

        samples["channel"].Should().Be("sunny_streams");
        samples["channel.display"].Should().Be("Sunny_Streams");
        samples["channel.id"].Should().Be("555001");
        samples["streamer"].Should().Be("Sunny_Streams");
        samples["user"].Should().Be("Moss_Mod");
        samples["user.name"].Should().Be("moss_mod");
        samples["user.id"].Should().Be("777002");
        samples["user.provider"].Should().Be("twitch");
        samples["user.link"].Should().Be("twitch.tv/moss_mod");
        samples["name"].Should().Be("Moss_Mod");
    }

    [Fact]
    public async Task Values_without_a_real_source_keep_a_neutral_example_that_is_not_a_real_person()
    {
        Dictionary<string, string> samples = await SamplesAsync(
            "sunny_streams",
            "Sunny_Streams",
            "555001",
            Viewer("moss_mod", "Moss_Mod", "777002")
        );

        samples["args.<n>"].Should().Be(TemplateHelperSamples.For("args.<n>")).And.NotBeEmpty();
        samples["target.name"].Should().Be("example_viewer");
        samples["user.followAge"].Should().Be(TemplateHelperSamples.For("user.followAge"));
    }

    [Fact]
    public void No_neutral_example_names_a_real_third_party()
    {
        string[] realPeople = ["kani", "stoney", "geennoob", "aaoa", "bamo", "kitte", "feather"];

        List<string> offenders =
        [
            .. TemplateHelperRegistry
                .All.Where(e =>
                    realPeople.Any(p => e.Sample.Contains(p, StringComparison.OrdinalIgnoreCase))
                )
                .Select(e => $"{e.Key} = {e.Sample}"),
        ];

        offenders.Should().BeEmpty("a picker example must never name a real person");
    }
}
