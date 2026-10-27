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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Arguments;

/// <summary>How the <c>args.&lt;n&gt;.user.*</c> rows read a person: once per argument, from the word typed.</summary>
public sealed class ArgumentUserHelpersBehaviorTests
{
    [Fact]
    public async Task Every_row_of_one_argument_shares_one_lookup_and_each_argument_gets_its_own()
    {
        CountingDirectory directory = new();
        ArgumentTemplateHelpers provider = Provider(directory);
        TemplateRenderScope scope = Scope("@ViewerTwo hello @StreamerFour");
        List<HelperNode> nodes =
        [
            Node("args.1.user.display"),
            Node("args.1.user.login"),
            Node("args.1.user.mention"),
            Node("args.2.user.display"),
            Node("args.2.user.login"),
        ];

        await provider.PrepareAsync(scope, nodes, CancellationToken.None);
        List<string?> rendered = [];
        foreach (HelperNode node in nodes)
            rendered.Add(await provider.RenderAsync(scope, node, CancellationToken.None));

        directory.Typed.Should().Equal("ViewerTwo", "hello");
        rendered.Should().Equal("ViewerTwo", "viewertwo", "@ViewerTwo", "hello", "hello");
    }

    [Fact]
    public async Task A_word_past_the_last_one_looks_nobody_up()
    {
        CountingDirectory directory = new();
        ArgumentTemplateHelpers provider = Provider(directory);
        TemplateRenderScope scope = Scope("@ViewerTwo");
        HelperNode node = Node("args.4.user.display");

        await provider.PrepareAsync(scope, [node], CancellationToken.None);
        string? rendered = await provider.RenderAsync(scope, node, CancellationToken.None);

        directory.Typed.Should().BeEmpty();
        rendered.Should().BeEmpty();
    }

    [Fact]
    public async Task A_key_that_is_not_a_user_row_is_left_to_the_other_helpers()
    {
        CountingDirectory directory = new();
        ArgumentTemplateHelpers provider = Provider(directory);
        TemplateRenderScope scope = Scope("@ViewerTwo");
        HelperNode node = Node("args.1.upper");

        await provider.PrepareAsync(scope, [node], CancellationToken.None);
        string? rendered = await provider.RenderAsync(scope, node, CancellationToken.None);

        directory.Typed.Should().BeEmpty();
        rendered.Should().Be("@VIEWERTWO");
    }

    private static ArgumentTemplateHelpers Provider(IViewerDirectory directory)
    {
        ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        return new ArgumentTemplateHelpers(
            services.GetRequiredService<IServiceScopeFactory>(),
            directory
        );
    }

    private static HelperNode Node(string key) => new(key, [], "{" + key + "}");

    private static TemplateRenderScope Scope(string arguments) =>
        new()
        {
            BroadcasterId = PeopleHelperFixture.ChannelA,
            Channel = null,
            Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["args"] = arguments,
                ["user.provider"] = "twitch",
            },
            Now = new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.Zero),
        };

    private sealed class CountingDirectory : IViewerDirectory
    {
        public List<string> Typed { get; } = [];

        public Task<ViewerRef?> ResolveAsync(
            IServiceProvider services,
            ViewerSide side,
            IReadOnlyDictionary<string, string> seeds,
            CancellationToken cancellationToken
        )
        {
            string typed = seeds.GetValueOrDefault("target")?.TrimStart('@') ?? string.Empty;
            if (typed.Length == 0)
                return Task.FromResult<ViewerRef?>(null);

            Typed.Add(typed);
            return Task.FromResult<ViewerRef?>(
                new ViewerRef(null, null, typed.ToLowerInvariant(), typed, "twitch")
            );
        }
    }
}
