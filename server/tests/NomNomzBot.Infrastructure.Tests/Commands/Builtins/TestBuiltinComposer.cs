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

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// The real <see cref="BuiltinResponseComposer"/> over the real <see cref="TestTemplateResolver"/> — so a test
/// asserts the exact line a slot speaks (override, platform text, tone, fallback) as the template engine fills it.
/// </summary>
internal static class TestBuiltinComposer
{
    public static IBuiltinResponseComposer Create(
        IChannelBuiltinReplyOverrides? channelReplies = null
    ) =>
        new BuiltinResponseComposer(
            TestTemplateResolver.Create(),
            NoPlatformBuiltinReplies.Instance,
            channelReplies ?? FakeChannelBuiltinReplies.None
        );
}
