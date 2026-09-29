// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
