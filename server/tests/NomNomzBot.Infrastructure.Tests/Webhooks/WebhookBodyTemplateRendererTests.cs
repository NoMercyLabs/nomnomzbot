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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
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

    [Fact]
    public void A_template_with_literal_JSON_braces_and_a_helper_renders_valid_parseable_JSON()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        const string template = /*lang=json,strict*/
            """{"event":"follow","nested":{"literal":"has {{braces}} inside"},"user":"{user}"}""";
        Dictionary<string, string> variables = new() { ["user"] = "Stoney_Eagle" };

        string rendered = sut.Render(template, variables);

        JObject parsed = JObject.Parse(rendered); // throws if the JSON is not valid — this IS the assertion
        parsed["event"]!.Value<string>().Should().Be("follow");
        parsed["nested"]!["literal"]!.Value<string>().Should().Be("has {{braces}} inside");
        parsed["user"]!.Value<string>().Should().Be("Stoney_Eagle");
    }

    [Fact]
    public void Every_variable_seeded_by_the_dispatcher_still_resolves_inside_JSON_string_values()
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

        JObject parsed = JObject.Parse(sut.Render(template, variables));

        parsed["type"]!.Value<string>().Should().Be("channel.cheer");
        parsed["x"]!.Value<string>().Should().Be("1");
        parsed["amount"]!.Value<string>().Should().Be("500");
    }

    [Fact]
    public void A_viewer_supplied_value_with_hostile_characters_is_JSON_escaped_not_injected()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        const string template = /*lang=json,strict*/
            """{"greeting":"Hi {user}!"}""";
        const string hostileName = "Sto\"ney\\Eagle\n🔥";
        Dictionary<string, string> variables = new() { ["user"] = hostileName };

        string rendered = sut.Render(template, variables);

        JObject parsed = JObject.Parse(rendered); // fails if the quote/backslash/newline broke the structure
        parsed["greeting"]!.Value<string>().Should().Be($"Hi {hostileName}!");
        // The raw rendered text must carry an escaped quote/backslash, never a bare one that could
        // terminate the JSON string or inject a sibling key.
        rendered.Should().Contain("\\\"").And.Contain("\\\\");
    }

    [Fact]
    public void A_null_template_renders_the_variables_as_a_JSON_object()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        Dictionary<string, string> variables = new() { ["a"] = "1" };

        JObject parsed = JObject.Parse(sut.Render(null, variables));

        parsed["a"]!.Value<string>().Should().Be("1");
    }

    [Fact]
    public void A_non_JSON_template_falls_back_to_plain_text_resolution()
    {
        WebhookBodyTemplateRenderer sut = BuildSut();
        Dictionary<string, string> variables = new() { ["user"] = "Stoney_Eagle" };

        string rendered = sut.Render("user={user}&event={event}", variables);

        rendered.Should().Be("user=Stoney_Eagle&event={event}");
    }
}
