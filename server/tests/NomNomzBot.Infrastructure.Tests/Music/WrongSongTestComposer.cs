// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
