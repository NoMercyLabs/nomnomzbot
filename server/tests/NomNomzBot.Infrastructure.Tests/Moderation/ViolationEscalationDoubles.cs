// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Moderation.Services;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>Test doubles for <see cref="IViolationEscalationService"/> shared by the handler and executor tests.</summary>
internal static class ViolationEscalationDoubles
{
    /// <summary>The ladder is not in play: the caller must run its own action.</summary>
    public static IViolationEscalationService NotHandled()
    {
        IViolationEscalationService service = Substitute.For<IViolationEscalationService>();
        service
            .TryEscalateAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(ViolationEscalationOutcome.NotHandled));
        return service;
    }

    /// <summary>The ladder took the offense and applied <paramref name="action"/>.</summary>
    public static IViolationEscalationService Handled(string action, bool applied = true)
    {
        IViolationEscalationService service = Substitute.For<IViolationEscalationService>();
        service
            .TryEscalateAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new ViolationEscalationOutcome(true, action, applied)));
        return service;
    }
}
