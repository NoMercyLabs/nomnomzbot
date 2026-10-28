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

using NomNomzBot.Application.Chat.Services;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;

/// <summary>A color service stub that returns the color the line carries and assigns nothing.</summary>
internal static class PassThroughChatColors
{
    public static IChatColorAssignmentService Create()
    {
        IChatColorAssignmentService colors = Substitute.For<IChatColorAssignmentService>();
        colors
            .ResolveAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult(callInfo.ArgAt<string?>(3)));
        return colors;
    }
}
