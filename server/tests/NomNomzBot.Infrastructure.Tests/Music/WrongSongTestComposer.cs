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
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>A response composer that renders the neutral fallback with its variables, as a channel with no overrides and no tone would.</summary>
internal static class WrongSongTestComposer
{
    public static IBuiltinResponseComposer Create()
    {
        IBuiltinResponseComposer composer = Substitute.For<IBuiltinResponseComposer>();
        composer
            .ComposeAsync(Arg.Any<BuiltinResponseRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                BuiltinResponseRequest request = call.Arg<BuiltinResponseRequest>();
                string text = request.NeutralFallback;
                foreach (
                    KeyValuePair<string, string> pair in request.Variables
                        ?? new Dictionary<string, string>()
                )
                    text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
                return Task.FromResult(text);
            });
        return composer;
    }
}
