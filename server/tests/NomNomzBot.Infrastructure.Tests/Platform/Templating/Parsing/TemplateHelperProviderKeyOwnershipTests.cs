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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Parsing;

/// <summary>
/// A provider handles only the keys its entries hold (the namespace no longer decides ownership), and a
/// helper whose entry takes lazy arguments renders only the arguments it asks for.
/// </summary>
public sealed class TemplateHelperProviderKeyOwnershipTests
{
    private static readonly Dictionary<string, string> Seeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user.name"] = "ViewerOne",
    };

    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddTemplateHelpers(typeof(StubUserProvider).Assembly)
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
    public async Task A_provider_for_the_user_namespace_renders_its_own_key_and_leaves_the_legacy_keys_alone()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        string rendered = await BuildResolver(registry)
            .ResolveAsync("{user.stubshout} {user.name}", Seeds, broadcasterId: null);

        rendered.Should().Be("shout ViewerOne");
        registry.ProviderFor("user.stubshout").Should().BeOfType<StubUserProvider>();
        registry.ProviderFor("user.name").Should().BeNull();
        registry
            .ForContext(TemplateHelperContext.Command)
            .Select(e => e.Key)
            .Should()
            .Contain(["user.stubshout", "user.name"]);
    }

    [Fact]
    public async Task A_provider_never_sees_a_legacy_key_of_its_namespace_that_no_seed_holds()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        string rendered = await BuildResolver(registry)
            .ResolveAsync("{transform.upper:abc} {transform.stubflip:abc}", Seeds, null);

        rendered.Should().Be("ABC cba");
        registry.ProviderFor("transform.upper:abc").Should().BeNull();
        registry.ProviderFor("transform.stubflip").Should().BeOfType<StubTransformProvider>();
    }

    [Fact]
    public void A_prefix_family_of_a_provider_claims_its_members()
    {
        using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        registry.ProviderFor("stubcount.dyn.abc").Should().BeOfType<StubCountingProvider>();
        registry.ProviderFor("stubcount.other").Should().BeNull();
    }

    [Fact]
    public async Task A_lazy_helper_gets_the_raw_arguments_and_only_the_chosen_branch_resolves()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        StubLazyProvider lazy = (StubLazyProvider)registry.ProviderFor("stublazy.pick")!;
        StubCountingProvider counting = (StubCountingProvider)
            registry.ProviderFor("stubcount.one")!;

        string rendered = await BuildResolver(registry)
            .ResolveAsync(
                "[{stublazy.pick:{stubcount.one}|X {stubcount.two} Y|{user.name}}]",
                Seeds,
                broadcasterId: null
            );

        rendered.Should().Be("[X count(two) Y]");
        lazy.SeenArguments.Should().Equal("{stubcount.one}", "X {stubcount.two} Y", "{user.name}");
        counting.PreparedKeys.Should().Equal("stubcount.two");
        counting.RenderedKeys.Should().Equal("stubcount.two");
    }

    [Fact]
    public async Task A_lazy_argument_renders_seeds_and_legacy_tokens_on_demand()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        string rendered = await BuildResolver(registry)
            .ResolveAsync(
                "{stublazy.last:{stubcount.one}|{user.name} {transform.upper:abc}}",
                Seeds,
                broadcasterId: null
            );

        rendered.Should().Be("ViewerOne ABC");
    }

    [Fact]
    public async Task A_helper_without_lazy_arguments_still_gets_rendered_arguments()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        StubCountingProvider counting = (StubCountingProvider)
            registry.ProviderFor("stubcount.one")!;

        string rendered = await BuildResolver(registry)
            .ResolveAsync("{stubcount.echo:{user.name}}", Seeds, broadcasterId: null);

        rendered.Should().Be("count(echo:ViewerOne)");
        counting.RenderedKeys.Should().Equal("stubcount.echo");
    }

    private static TemplateHelperEntry Row(string key, string? prefix = null, bool lazy = false) =>
        new(key, [TemplateHelperContext.Command], new LocalizedText("template.helper.x"), prefix)
        {
            LazyArguments = lazy,
        };

    /// <summary>A provider for the <c>user</c> namespace that holds one key only.</summary>
    public sealed class StubUserProvider : ITemplateHelperProvider
    {
        public string Namespace => "user";

        public IReadOnlyList<TemplateHelperEntry> Entries { get; } = [Row("user.stubshout")];

        public ValueTask PrepareAsync(
            TemplateRenderScope scope,
            IReadOnlyCollection<HelperNode> nodes,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;

        public ValueTask<string?> RenderAsync(
            TemplateRenderScope scope,
            HelperNode node,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<string?>(node.Key == "user.stubshout" ? "shout" : null);
    }

    /// <summary>A provider for the <c>transform</c> namespace; the legacy <c>transform.upper</c> stays legacy.</summary>
    public sealed class StubTransformProvider : ITemplateHelperProvider
    {
        public string Namespace => "transform";

        public IReadOnlyList<TemplateHelperEntry> Entries { get; } = [Row("transform.stubflip")];

        public ValueTask PrepareAsync(
            TemplateRenderScope scope,
            IReadOnlyCollection<HelperNode> nodes,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;

        public ValueTask<string?> RenderAsync(
            TemplateRenderScope scope,
            HelperNode node,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<string?>(
                node.Key == "transform.stubflip" ? string.Concat(node.Arguments[0].Reverse()) : null
            );
    }

    /// <summary>Records which tokens were prepared and rendered, so a branch that never ran shows.</summary>
    public sealed class StubCountingProvider : ITemplateHelperProvider
    {
        public List<string> PreparedKeys { get; } = [];

        public List<string> RenderedKeys { get; } = [];

        public string Namespace => "stubcount";

        public IReadOnlyList<TemplateHelperEntry> Entries { get; } =
        [
            Row("stubcount.one"),
            Row("stubcount.two"),
            Row("stubcount.echo"),
            Row("stubcount.dyn.<name>", prefix: "stubcount.dyn."),
        ];

        public ValueTask PrepareAsync(
            TemplateRenderScope scope,
            IReadOnlyCollection<HelperNode> nodes,
            CancellationToken cancellationToken
        )
        {
            PreparedKeys.AddRange(nodes.Select(n => n.Key));
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> RenderAsync(
            TemplateRenderScope scope,
            HelperNode node,
            CancellationToken cancellationToken
        )
        {
            RenderedKeys.Add(node.Key);
            string text =
                node.Arguments.Count == 0
                    ? $"count({node.Key["stubcount.".Length..]})"
                    : $"count({node.Key["stubcount.".Length..]}:{string.Join(",", node.Arguments)})";
            return ValueTask.FromResult<string?>(text);
        }
    }

    /// <summary>
    /// <c>pick</c> renders its second argument and <c>last</c> its last one; both take lazy arguments and
    /// leave every other argument unrendered.
    /// </summary>
    public sealed class StubLazyProvider : ITemplateHelperProvider
    {
        public List<string> SeenArguments { get; } = [];

        public string Namespace => "stublazy";

        public IReadOnlyList<TemplateHelperEntry> Entries { get; } =
        [Row("stublazy.pick", lazy: true), Row("stublazy.last", lazy: true)];

        public ValueTask PrepareAsync(
            TemplateRenderScope scope,
            IReadOnlyCollection<HelperNode> nodes,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;

        public async ValueTask<string?> RenderAsync(
            TemplateRenderScope scope,
            HelperNode node,
            CancellationToken cancellationToken
        )
        {
            SeenArguments.AddRange(node.Arguments);
            int index = node.Key == "stublazy.pick" ? 1 : node.Arguments.Count - 1;
            return await node.RenderArgumentAsync!(index, cancellationToken);
        }
    }
}
