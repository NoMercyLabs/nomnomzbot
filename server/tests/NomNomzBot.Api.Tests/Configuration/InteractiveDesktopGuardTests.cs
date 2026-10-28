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

using FluentAssertions;
using NomNomzBot.Api.Configuration;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// Proves the shared interactive-desktop predicate that both the startup-error dialog and the system-tray icon gate
/// on: a human session requires an interactive process AND a console redirected on neither stream. A redirected
/// stdout or stdin (CI, a pipe, a service, a container) makes it false — that's exactly how an automation boot
/// avoids popping a dialog or a tray.
/// </summary>
public sealed class InteractiveDesktopGuardTests
{
    [Theory]
    [InlineData(true, false, false, true)] // interactive + no redirection ⇒ a real desktop session
    [InlineData(false, false, false, false)] // non-interactive (service) ⇒ never
    [InlineData(true, true, false, false)] // stdout redirected (piped/CI) ⇒ never
    [InlineData(true, false, true, false)] // stdin redirected (automation) ⇒ never
    [InlineData(true, true, true, false)] // both redirected ⇒ never
    [InlineData(false, true, true, false)] // non-interactive + redirected ⇒ never
    public void IsInteractiveDesktop_requires_interactive_and_no_redirection(
        bool userInteractive,
        bool outputRedirected,
        bool inputRedirected,
        bool expected
    )
    {
        bool result = InteractiveDesktopGuard.IsInteractiveDesktop(
            userInteractive,
            outputRedirected,
            inputRedirected
        );

        result.Should().Be(expected);
    }
}
