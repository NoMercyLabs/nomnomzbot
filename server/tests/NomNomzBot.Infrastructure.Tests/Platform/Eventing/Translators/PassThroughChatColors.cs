// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
