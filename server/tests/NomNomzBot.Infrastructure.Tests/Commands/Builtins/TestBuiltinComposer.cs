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

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// The real <see cref="BuiltinResponseComposer"/> over a resolver that only fills <c>{variables}</c> — so a test
/// asserts the exact line a slot speaks (override, platform text, tone, fallback) without a template engine.
/// </summary>
internal static class TestBuiltinComposer
{
    public static IBuiltinResponseComposer Create(
        FakeChannelBuiltinReplies? channelReplies = null
    ) =>
        new BuiltinResponseComposer(
            FillingResolver(),
            NoPlatformBuiltinReplies.Instance,
            channelReplies ?? FakeChannelBuiltinReplies.None
        );

    private static ITemplateResolver FillingResolver()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> pair in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{pair.Key}}}", pair.Value);
                return Task.FromResult(template);
            });
        return resolver;
    }
}
