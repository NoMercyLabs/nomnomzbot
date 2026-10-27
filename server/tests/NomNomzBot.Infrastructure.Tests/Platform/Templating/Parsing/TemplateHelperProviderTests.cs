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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Parsing;

/// <summary>
/// The provider shape end to end: the assembly scan finds a stub provider, the registry lists it, and the
/// real resolver hands it fully rendered arguments with one <c>PrepareAsync</c> per fetch group.
/// </summary>
public sealed class TemplateHelperProviderTests
{
    private static readonly Dictionary<string, string> Seeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user"] = "ViewerOne",
    };

    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddTemplateHelpers(typeof(StubEchoProvider).Assembly)
            .BuildServiceProvider();

    private static TemplateResolver BuildResolver(ITemplateHelperRegistry registry) =>
        new(
            new ServiceCollection()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.Zero)),
            registry
        );

    [Fact]
    public async Task The_scan_finds_the_stub_and_the_resolver_renders_its_tokens_with_rendered_arguments()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        StubEchoProvider stub = (StubEchoProvider)registry.ProviderFor("stubecho.say")!;

        string rendered = await BuildResolver(registry)
            .ResolveAsync("{stubecho.say:{user}|x} {stubecho.speak:y}", Seeds, broadcasterId: null);

        rendered.Should().Be("echo(ViewerOne,x) echo(y)");
        // The token with {user} in an argument has no final text before {user} rendered: it prepares alone,
        // after the statically known token, and still receives its rendered arguments.
        stub.PrepareCalls.Should().HaveCount(2);
        stub.PrepareCalls[0].Select(n => n.Key).Should().Equal("stubecho.speak");
        stub.PrepareCalls[0][0].Arguments.Should().Equal("y");
        stub.PrepareCalls[1].Select(n => n.Key).Should().Equal("stubecho.say");
        stub.PrepareCalls[1][0].Arguments.Should().Equal("ViewerOne", "x");
    }

    [Fact]
    public async Task Tokens_known_before_the_render_prepare_once_with_every_node_of_the_fetch_group()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        StubEchoProvider stub = (StubEchoProvider)registry.ProviderFor("stubecho.say")!;

        string rendered = await BuildResolver(registry)
            .ResolveAsync("{stubecho.say:a|x} {stubecho.speak:y}", Seeds, broadcasterId: null);

        rendered.Should().Be("echo(a,x) echo(y)");
        stub.PrepareCalls.Should().HaveCount(1);
        stub.PrepareCalls[0].Select(n => n.Key).Should().Equal("stubecho.say", "stubecho.speak");
        stub.PrepareCalls[0][0].Arguments.Should().Equal("a", "x");
        stub.PrepareCalls[0][1].Arguments.Should().Equal("y");
    }

    [Fact]
    public void The_registry_lists_the_stub_rows_for_their_context_and_finds_each_by_its_own_key()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        registry
            .ForContext(TemplateHelperContext.Command)
            .Select(e => e.Key)
            .Should()
            .Contain("stubecho.say");
        registry.Find("stubecho.speak")!.Key.Should().Be("stubecho.speak");
        registry.Find("stubecho.say")!.Source.Should().Be("echo");
        registry.Find("stubecho.shout").Should().BeNull("a key no row holds does not resolve");
    }

    [Fact]
    public async Task A_seed_with_the_same_key_wins_and_the_provider_is_not_called()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        StubEchoProvider stub = (StubEchoProvider)registry.ProviderFor("stubecho.say")!;
        Dictionary<string, string> seeds = new(Seeds, StringComparer.OrdinalIgnoreCase)
        {
            ["stubecho.say"] = "SEEDED",
        };

        string rendered = await BuildResolver(registry)
            .ResolveAsync("{stubecho.say}", seeds, broadcasterId: null);

        rendered.Should().Be("SEEDED");
        stub.PrepareCalls.Should().BeEmpty();
        stub.RenderCount.Should().Be(0);
    }

    [Fact]
    public async Task A_provider_returning_null_prints_the_token_as_written()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        string rendered = await BuildResolver(registry)
            .ResolveAsync("{stubecho.say:none}", Seeds, broadcasterId: null);

        rendered.Should().Be("{stubecho.say:none}");
    }

    /// <summary>Found by the scan like any real provider; records what the resolver hands it.</summary>
    public sealed class StubEchoProvider : ITemplateHelperProvider
    {
        public List<IReadOnlyList<HelperNode>> PrepareCalls { get; } = [];

        public int RenderCount { get; private set; }

        public string Namespace => "stubecho";

        public IReadOnlyList<TemplateHelperEntry> Entries { get; } =
        [
            new TemplateHelperEntry(
                "stubecho.say",
                [TemplateHelperContext.Command],
                new LocalizedText("template.helper.x")
            )
            {
                Source = "echo",
            },
            new TemplateHelperEntry(
                "stubecho.speak",
                [TemplateHelperContext.Command],
                new LocalizedText("template.helper.y")
            )
            {
                Source = "echo",
            },
        ];

        public ValueTask PrepareAsync(
            TemplateRenderScope scope,
            IReadOnlyCollection<HelperNode> nodes,
            CancellationToken cancellationToken
        )
        {
            PrepareCalls.Add([.. nodes]);
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> RenderAsync(
            TemplateRenderScope scope,
            HelperNode node,
            CancellationToken cancellationToken
        )
        {
            RenderCount++;
            string? text = node.Arguments is ["none"]
                ? null
                : $"echo({string.Join(",", node.Arguments)})";
            return ValueTask.FromResult(text);
        }
    }
}
