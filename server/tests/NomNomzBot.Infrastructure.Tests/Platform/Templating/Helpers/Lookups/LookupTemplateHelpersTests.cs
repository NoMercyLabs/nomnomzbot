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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;
using NomNomzBot.Infrastructure.Tests.Localization;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Lookups;

public sealed class LookupTemplateHelpersTests
{
    [Fact]
    public async Task The_provider_owns_the_lookup_namespace_and_the_scan_registers_it()
    {
        ServiceProvider services = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();

        ITemplateHelperProvider? provider = services
            .GetRequiredService<ITemplateHelperRegistry>()
            .ProviderFor("lookup.weather");

        provider.Should().NotBeNull();
        provider!.Namespace.Should().Be("lookup");
        await services.DisposeAsync();
    }

    [Fact]
    public async Task A_slow_source_renders_the_fallback_string_instead_of_blocking_chat()
    {
        await using LookupHelperFixture fixture = new(hang: true);

        string currency = await fixture.RenderAsync("{lookup.currency:10|EUR|USD}");
        string weather = await fixture.RenderAsync("{lookup.weather:Amsterdam}");
        string translation = await fixture.RenderAsync("{lookup.translate:nl|good morning}");

        currency.Should().Be("(exchange rate unavailable)");
        weather.Should().Be("(weather unavailable)");
        translation.Should().Be("(translation unavailable)");
    }

    [Fact]
    public async Task A_failing_source_renders_the_fallback_in_the_channel_language()
    {
        await using LookupHelperFixture fixture = new(language: "nl");

        string unknownCity = await fixture.RenderAsync("{lookup.weather:Nowhereville}");
        string unknownPair = await fixture.RenderAsync("{lookup.currency:10|EUR|JPY}");

        unknownCity.Should().Be("(weer niet beschikbaar)");
        unknownPair.Should().Be("(wisselkoers niet beschikbaar)");
    }

    [Fact]
    public async Task A_dutch_channel_reads_the_weather_and_the_date_in_dutch()
    {
        await using LookupHelperFixture fixture = new(language: "nl");

        (await fixture.RenderAsync("{lookup.weather:Amsterdam}"))
            .Should()
            .Be("Amsterdam: 12 °C, lichte regen");
        (await fixture.RenderAsync("{lookup.weather:Amsterdam.wind}")).Should().Be("18 km/h ZW");
        (await fixture.RenderAsync("{lookup.time:Tokyo.date}")).Should().Be("maandag 26 oktober");
        (await fixture.RenderAsync("{lookup.currency:10|EUR|USD}"))
            .Should()
            .Be("10 EUR = 11,62 USD");
    }

    [Fact]
    public async Task Saas_mode_switches_weather_and_local_time_off_and_never_calls_open_meteo()
    {
        await using LookupHelperFixture fixture = new(openMeteoAllowed: false);

        (await fixture.RenderAsync("{lookup.weather:Amsterdam}"))
            .Should()
            .Be("(weather unavailable)");
        (await fixture.RenderAsync("{lookup.weather:Amsterdam.tomorrow}"))
            .Should()
            .Be("(weather unavailable)");
        (await fixture.RenderAsync("{lookup.time:Tokyo}")).Should().Be("(local time unavailable)");
        (await fixture.RenderAsync("{lookup.currency.rate:EUR|USD}")).Should().Be("1.162");

        fixture.Handler.CountFor("geocoding-api.open-meteo.com").Should().Be(0);
        fixture.Handler.CountFor("api.open-meteo.com").Should().Be(0);
    }

    [Theory]
    [InlineData(DeploymentMode.Saas, false)]
    [InlineData(DeploymentMode.SelfHostLite, true)]
    [InlineData(DeploymentMode.SelfHostFull, true)]
    public async Task The_host_allows_open_meteo_everywhere_except_in_saas_mode(
        DeploymentMode mode,
        bool allowed
    )
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(new DeploymentContext(mode))
            .BuildServiceProvider();

        LookupSources sources = LookupSources.FromServices(
            services.GetRequiredService<IServiceScopeFactory>()
        );

        sources.OpenMeteoAllowed.Should().Be(allowed);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task A_second_render_of_the_same_city_is_served_from_the_cache()
    {
        await using LookupHelperFixture fixture = new();

        await fixture.RenderAsync("{lookup.weather:Amsterdam.temp}");
        await fixture.RenderAsync("{lookup.weather:Amsterdam.humidity}");

        fixture.Handler.CountFor("geocoding-api.open-meteo.com").Should().Be(1);
        fixture.Handler.CountFor("api.open-meteo.com").Should().Be(1);
    }

    [Fact]
    public async Task Every_request_carries_the_product_user_agent()
    {
        await using LookupHelperFixture fixture = new();

        await fixture.RenderAsync("{lookup.weather:Amsterdam}{lookup.currency.rate:EUR|USD}");
        await fixture.RenderAsync("{lookup.translate:nl|good morning}");

        fixture.Handler.Requests.Should().HaveCount(4);
        fixture
            .Handler.Requests.Select(r => r.Headers.UserAgent.ToString())
            .Should()
            .OnlyContain(agent => agent.StartsWith("NomNomzBot/"));
    }

    [Fact]
    public async Task Tokens_about_the_same_user_in_one_render_read_twitch_once()
    {
        await using LookupHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{lookup.twitch:viewertwo.display} {lookup.twitch:viewertwo.createdAt} {lookup.twitch:viewertwo.game}"
        );

        rendered.Should().Be("ViewerTwo 2019-05-02 Minecraft");
        await fixture
            .Users.Received(1)
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_twitch_failure_renders_the_twitch_fallback_and_an_offline_channel_does_not()
    {
        await using LookupHelperFixture down = new(twitchDown: true);

        (await down.RenderAsync("{lookup.twitch:viewertwo.display}"))
            .Should()
            .Be("(Twitch lookup unavailable)");
        (await down.RenderAsync("{lookup.twitch:viewertwo.isLive}"))
            .Should()
            .Be("(Twitch lookup unavailable)");

        await using LookupHelperFixture up = new();
        (await up.RenderAsync("{lookup.twitch:viewertwo.isLive}")).Should().Be("false");
    }

    [Fact]
    public async Task The_follower_count_and_followage_read_only_the_channel_the_template_runs_in()
    {
        await using LookupHelperFixture fixture = new();

        await fixture.RenderAsync(
            "{lookup.twitch:viewertwo.followers} {lookup.twitch:viewertwo.followage:othercha}"
        );

        await fixture
            .Channels.DidNotReceive()
            .GetChannelFollowerCountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await fixture
            .Channels.DidNotReceive()
            .GetChannelFollowerAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        await using LookupHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();

        fixture.Provider.Entries.Should().HaveCount(10);
        foreach (TemplateHelperEntry entry in fixture.Provider.Entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNullOrEmpty(entry.Key);
        }
    }

    [Fact]
    public async Task Every_fallback_string_of_the_code_equals_its_english_and_dutch_resource()
    {
        DashboardStringsXmlCatalog strings = new();

        foreach (string source in LookupFallbacks.Sources)
        {
            string key = LookupFallbacks.ResourceName(source);
            strings.TryGetEnglish(key, out string english).Should().BeTrue(key);
            strings.TryGetDutch(key, out string dutch).Should().BeTrue(key);
            english.Should().Be(LookupFallbacks.English(source), key);
            dutch.Should().Be(LookupFallbacks.Dutch(source), key);
            dutch.Should().NotBe(english, key);
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Every_sample_is_plain_text_that_renders_to_itself_through_the_resolver()
    {
        await using LookupHelperFixture fixture = new();

        foreach (TemplateHelperEntry entry in fixture.Provider.Entries)
        {
            string rendered = await fixture.RenderAsync(entry.Sample);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }
}
