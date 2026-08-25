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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves the <c>{transform.&lt;name&gt;:&lt;text&gt;}</c> template helper end to end through the real
/// <see cref="TemplateResolver"/>: transforms compose/chain, an unknown transform name renders a visible
/// recorded failure rather than a silent no-op, and the five old-bot text-transform commands (!yell,
/// !whisper, !slow, !mock, !dramatic) are each expressible from these primitives alone — zero new ones.
/// </summary>
public sealed class TextTransformTemplateResolverTests
{
    private readonly TemplateResolver _resolver;

    public TextTransformTemplateResolverTests()
    {
        ServiceCollection services = new();
        services.AddLogging();
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    [Fact]
    public async Task Transforms_Chain_InnermostResolvesFirst()
    {
        // reverse("hello") = "olleh", then upper("olleh") = "OLLEH" — the outer transform only becomes
        // self-contained once the inner one has been peeled off in a prior loop iteration.
        string resolved = await _resolver.ResolveAsync(
            "{transform.upper:{transform.reverse:hello}}",
            new Dictionary<string, string>(),
            broadcasterId: null
        );

        resolved.Should().Be("OLLEH");
    }

    [Fact]
    public async Task Transforms_ChainOverAVariablePlaceholder_ComposesWithNormalResolution()
    {
        string resolved = await _resolver.ResolveAsync(
            "{transform.upper:{transform.reverse:{message}}}",
            new Dictionary<string, string> { ["message"] = "hello" },
            broadcasterId: null
        );

        resolved.Should().Be("OLLEH");
    }

    [Fact]
    public async Task UnknownTransform_RendersAVisibleRecordedFailure_NotTheRawInputUnchanged()
    {
        string resolved = await _resolver.ResolveAsync(
            "Yo {transform.frobnicate:hello}!",
            new Dictionary<string, string>(),
            broadcasterId: null
        );

        // Never silently falls back to "hello" (the input) or the raw "{transform.frobnicate:hello}" token —
        // it must be an unmistakable, visible failure marker naming the bad transform.
        resolved.Should().Be("Yo [transform error: Unknown text transform 'frobnicate'.]!");
        resolved.Should().NotContain("{transform.frobnicate:hello}");
        resolved.Should().NotBe("Yo hello!");
    }

    [Theory]
    [InlineData("watch this", "{transform.upper:{message}}", "WATCH THIS")] // !yell
    [InlineData("WATCH THIS", "{transform.lower:{message}}", "watch this")] // !whisper
    [InlineData("wow", "{transform.spaced:{message}}", "w o w")] // !slow
    [InlineData("really", "{transform.alternating:{message}}", "ReAlLy")] // !mock
    [InlineData("wow", "{transform.spaced:{transform.upper:{message}}}", "W O W")] // !dramatic
    public async Task OldBotCommandShapes_AreExpressibleFromExistingPrimitives_ZeroNewOnes(
        string message,
        string template,
        string expected
    )
    {
        string resolved = await _resolver.ResolveAsync(
            template,
            new Dictionary<string, string> { ["message"] = message },
            broadcasterId: null
        );

        resolved.Should().Be(expected);
    }
}
