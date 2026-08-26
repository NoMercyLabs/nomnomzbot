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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// S022b done-when: <c>{provider}</c> in a message template renders the platform key that actually
/// delivered the event, end to end — real event -&gt; real event handler -&gt; the SAME variables dict
/// <see cref="IEventResponseExecutor"/> receives -&gt; real <see cref="TemplateResolver"/> substitution.
/// Asserts the RENDERED STRING, never merely that the variables dictionary contains a key (that surface
/// check already lives in <c>NewSubscriptionEventHandlerTests</c> from S022).
/// </summary>
public sealed class ProviderTemplateHelperTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d502");
    private static readonly TemplateResolver Resolver = new(
        Substitute.For<IServiceScopeFactory>(),
        Substitute.For<IChannelRegistry>(),
        NullLogger<TemplateResolver>.Instance,
        TimeProvider.System
    );

    private static async Task<Dictionary<string, string>> CaptureSubscriptionVariablesAsync(
        string? provider
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();

        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(executor)
            .BuildServiceProvider();

        NewSubscriptionEventHandler handler = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IPipelineEngine>(),
            NullLogger<NewSubscriptionEventHandler>.Instance
        );

        NewSubscriptionEvent subEvent = provider is null
            ? new()
            {
                BroadcasterId = Channel,
                UserId = "tw-42",
                UserDisplayName = "SubTester",
                Tier = "1000",
            }
            : new()
            {
                BroadcasterId = Channel,
                UserId = provider == AuthEnums.Platform.Kick ? "kick-42" : "tw-42",
                UserDisplayName = "SubTester",
                Tier = "1000",
                Provider = provider,
            };

        await handler.HandleAsync(subEvent);

        Dictionary<string, string> captured =
            executor
                .ReceivedCalls()
                .Where(call =>
                    call.GetMethodInfo().Name == nameof(IEventResponseExecutor.ExecuteAsync)
                )
                .Select(call => (Dictionary<string, string>)call.GetArguments()[4]!)
                .FirstOrDefault()
            ?? throw new InvalidOperationException("EventResponseExecutor was never invoked.");

        captured["provider"]
            .Should()
            .Be(
                provider ?? AuthEnums.Platform.Twitch,
                "the handler must seed the SAME provider it received onto the event-response variables"
            );

        return captured;
    }

    [Fact]
    public async Task Kick_delivered_subscription_renders_provider_as_kick()
    {
        Dictionary<string, string> variables = await CaptureSubscriptionVariablesAsync(
            AuthEnums.Platform.Kick
        );

        string rendered = Resolver.Resolve("Delivered via {provider}!", variables);

        rendered.Should().Be("Delivered via kick!");
    }

    [Fact]
    public async Task Twitch_delivered_subscription_renders_provider_as_twitch()
    {
        Dictionary<string, string> variables = await CaptureSubscriptionVariablesAsync(
            AuthEnums.Platform.Twitch
        );

        string rendered = Resolver.Resolve("Delivered via {provider}!", variables);

        rendered.Should().Be("Delivered via twitch!");
    }

    [Fact]
    public void Missing_provider_source_leaves_the_placeholder_literal_instead_of_a_blank()
    {
        // The honest edge (S022b requirement 4): a template rendered outside any provider-scoped event
        // context (no "provider" seed) must never silently render an empty or misleading platform name.
        // TemplateResolver's generic unknown-key behavior — leave the placeholder text untouched — is the
        // correct signal here: the author sees "{provider}" literally in the output instead of a false
        // "twitch" default or a blank gap, making the missing-context bug visible instead of hidden.
        Dictionary<string, string> noProviderContext = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = "SomeChatter",
        };

        string rendered = Resolver.Resolve("Delivered via {provider}!", noProviderContext);

        rendered.Should().Be("Delivered via {provider}!");
    }
}
