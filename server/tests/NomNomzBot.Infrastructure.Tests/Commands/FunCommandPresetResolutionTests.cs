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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Commands.Presets;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves every fun-command preset reads correctly in chat: each response goes through the real
/// <see cref="TemplateResolver"/> with the variables the chat path seeds for a command, and no literal brace
/// may reach the viewer (the resolver is single-brace, so <c>{{args.1}}</c> would come out as <c>{Bob}</c>).
/// </summary>
public sealed class FunCommandPresetResolutionTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000f301");

    private readonly TemplateResolver _resolver;

    public FunCommandPresetResolutionTests()
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(PronounGrammarTestDbContext.New());
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private Task<string> ResolveAsync(string template)
    {
        Dictionary<string, string> vars = ChatMessageHandler.BuildArgumentVariables("Bob");
        vars["user"] = "Stoney";
        vars["user.name"] = "Stoney";
        return _resolver.ResolveAsync(template, vars, Channel);
    }

    [Fact]
    public async Task EveryFunPresetResponse_ResolvesWithoutALiteralBrace()
    {
        foreach (CreateCommandDto preset in FunCommandPresets.All)
        {
            List<string> templates = [.. preset.TemplateResponses ?? []];
            if (preset.TemplateResponse is not null)
                templates.Add(preset.TemplateResponse);

            templates.Should().NotBeEmpty($"preset {preset.Name} must have a response");
            foreach (string template in templates)
            {
                string resolved = await ResolveAsync(template);
                resolved
                    .Should()
                    .NotContainAny(
                        ["{", "}"],
                        $"preset {preset.Name} / '{template}' reached chat as '{resolved}'"
                    );
            }
        }
    }

    [Fact]
    public async Task TheHugPreset_ReadsAsAHugBetweenTheSenderAndTheTarget()
    {
        CreateCommandDto hug = FunCommandPresets.Find("hug")!;

        string resolved = await ResolveAsync(hug.TemplateResponse!);

        resolved.Should().Be("Stoney gives Bob a big warm hug! 🤗");
    }
}
