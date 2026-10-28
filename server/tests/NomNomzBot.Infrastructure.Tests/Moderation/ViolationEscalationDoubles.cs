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
