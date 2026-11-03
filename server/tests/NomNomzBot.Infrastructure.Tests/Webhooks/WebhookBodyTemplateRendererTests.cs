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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves S-WEBHOOK-TEMPLATE-GRAMMAR: outbound webhook bodies now render through the single
/// <see cref="ITemplateResolver"/> grammar via a JSON-aware substitution that cannot corrupt the payload —
/// placeholders are resolved on the parsed JSON tree (string leaves only), so a hostile value never escapes
/// its string, and every variable webhook bodies rely on today (channel/stream/random/time/count/transform
/// helpers, plus the caller-seeded event variables) still resolves. A template that is not JSON at all falls
/// back to plain-text resolution.
/// </summary>
public sealed class WebhookBodyTemplateRendererTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static WebhookBodyTemplateRenderer BuildSut()
    {
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        ILogger<TemplateResolver> logger = Substitute.For<ILogger<TemplateResolver>>();
        TemplateResolver resolver = new(scopeFactory, registry, logger, new FakeTimeProvider(Now));
        return new WebhookBodyTemplateRenderer(resolver);
    }

    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000f003");

    /// <summary>The real helper registry and resolver, with a channel the registry knows.</summary>
    private static ServiceProvider RegistryServices()
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels
            .Get(Channel)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Channel,
                    TwitchChannelId = "555003",
                    ChannelName = "samplestreamer",
                    DisplayName = "SampleStreamer",
                    CurrentTitle = "Sample title",
                }
            );
        ServiceCollection services = new();
        services.AddSingleton(channels);
        services.AddSingleton<IApplicationDbContext>(ViewerDataTestDbContext.New());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddSingleton(new Random(1));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddTemplateHelpers(typeof(TemplateResolver).Assembly);
        return services.BuildServiceProvider();
    }

    private static WebhookBodyTemplateRenderer BuildRegistrySut(ServiceProvider services) =>
        new(
            new TemplateResolver(
                services.GetRequiredService<IServiceScopeFactory>(),
                services.GetRequiredService<IChannelRegistry>(),
                NullLogger<TemplateResolver>.Instance,
                new FakeTimeProvider(Now),
                services.GetRequiredService<ITemplateHelperRegistry>()
            )
        );

    [Fact]
    public async Task A_JSON_body_resolves_registry_helpers_for_the_channel()
    {
        await using ServiceProvider services = RegistryServices();
        WebhookBodyTemplateRenderer sut = BuildRegistrySut(services);
        const string template = /*lang=json,strict*/
            """{"channel":"{channel}","title":"{stream.title}"}""";

        string rendered = await sut.RenderAsync(
            template,
            new Dictionary<string, string>(),
            true,
            Channel
        );

        JObject parsed = JObject.Parse(rendered);
        parsed["channel"]!.Value<string>().Should().Be("samplestreamer");
        parsed["title"]!.Value<string>().Should().Be("Sample title");
    }

    [Fact]
    public async Task A_plain_text_body_resolves_registry_helpers_for_the_channel()
    {
        await using ServiceProvider services = RegistryServices();
        WebhookBodyTemplateRenderer sut = BuildRegistrySut(services);

        string rendered = await sut.RenderAsync(
            "live: {channel}",
            new Dictionary<string, string>(),
            false,
            Channel
        );

        rendered.Should().Be("live: samplestreamer");
    }

    [Fact]
    public async Task A_template_with_literal_JSON_braces_and_a_helper_renders_valid_parseable_JSON()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        const string template = /*lang=json,strict*/
            """{"event":"follow","nested":{"literal":"has {{braces}} inside"},"user":"{user}"}""";
        Dictionary<string, string> variables = new() { ["user"] = "Stoney_Eagle" };

        string rendered = await sut.RenderAsync(template, variables, true, Channel);

        JObject parsed = JObject.Parse(rendered); // throws if the JSON is not valid — this IS the assertion
        parsed["event"]!.Value<string>().Should().Be("follow");
        parsed["nested"]!["literal"]!.Value<string>().Should().Be("has {{braces}} inside");
        parsed["user"]!.Value<string>().Should().Be("Stoney_Eagle");
    }

    [Fact]
    public async Task Every_variable_seeded_by_the_dispatcher_still_resolves_inside_JSON_string_values()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        const string template = /*lang=json,strict*/
            """{"type":"{event_type}","x":"{x}","amount":"{amount}"}""";
        Dictionary<string, string> variables = new()
        {
            ["event_type"] = "channel.cheer",
            ["x"] = "1",
            ["amount"] = "500",
        };

        JObject parsed = JObject.Parse(await sut.RenderAsync(template, variables, true, Channel));

        parsed["type"]!.Value<string>().Should().Be("channel.cheer");
        parsed["x"]!.Value<string>().Should().Be("1");
        parsed["amount"]!.Value<string>().Should().Be("500");
    }

    [Fact]
    public async Task A_viewer_supplied_value_with_hostile_characters_is_JSON_escaped_not_injected()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        const string template = /*lang=json,strict*/
            """{"greeting":"Hi {user}!"}""";
        const string hostileName = "Sto\"ney\\Eagle\n🔥";
        Dictionary<string, string> variables = new() { ["user"] = hostileName };

        string rendered = await sut.RenderAsync(template, variables, true, Channel);

        JObject parsed = JObject.Parse(rendered); // fails if the quote/backslash/newline broke the structure
        parsed["greeting"]!.Value<string>().Should().Be($"Hi {hostileName}!");
        // The raw rendered text must carry an escaped quote/backslash, never a bare one that could
        // terminate the JSON string or inject a sibling key.
        rendered.Should().Contain("\\\"").And.Contain("\\\\");
    }

    [Fact]
    public async Task A_null_template_renders_the_variables_as_a_JSON_object()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        Dictionary<string, string> variables = new() { ["a"] = "1" };

        JObject parsed = JObject.Parse(await sut.RenderAsync(null, variables, true, Channel));

        parsed["a"]!.Value<string>().Should().Be("1");
    }

    [Fact]
    public async Task A_template_declared_as_non_JSON_renders_through_the_plain_text_path()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        Dictionary<string, string> variables = new() { ["user"] = "Stoney_Eagle" };

        string rendered = await sut.RenderAsync(
            "user={user}&event={event}",
            variables,
            false,
            Channel
        );

        rendered.Should().Be("user=Stoney_Eagle&event={event}");
    }

    [Fact]
    public async Task A_template_declared_as_JSON_that_fails_to_parse_throws_naming_the_position()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        Dictionary<string, string> variables = new() { ["user"] = "Stoney_Eagle" };
        // Missing colon — a syntax error an author who intended JSON would want to be told about,
        // not have silently downgraded to the unescaped plain-text path (S-WEBHOOK-JSON-FALLBACK).
        const string brokenJson = /*lang=json,strict*/
            """{"who" "{user}"}""";

        Func<Task> act = () => sut.RenderAsync(brokenJson, variables, true, Channel);

        await act.Should()
            .ThrowAsync<WebhookBodyTemplateInvalidJsonException>()
            .WithMessage("*line*");
    }

    [Fact]
    public async Task A_template_that_merely_looks_like_JSON_but_is_declared_non_JSON_never_attempts_to_parse()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        Dictionary<string, string> variables = new() { ["user"] = "Stoney_Eagle" };
        // Structurally broken as JSON (missing colon) — but since BodyIsJson is false, the renderer must
        // never even attempt the JSON parse, so this must NOT throw.
        const string brokenLookingJson = /*lang=json,strict*/
            """{"who" "{user}"}""";

        Func<Task> act = () => sut.RenderAsync(brokenLookingJson, variables, false, Channel);

        await act.Should().NotThrowAsync<WebhookBodyTemplateInvalidJsonException>();
    }
}
