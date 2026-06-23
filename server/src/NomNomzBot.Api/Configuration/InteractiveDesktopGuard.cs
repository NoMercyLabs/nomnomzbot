// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// The single predicate for "a human is sitting at an interactive desktop session right now" — the gate both the
/// startup-error dialog and the system-tray icon use to decide whether to surface any UI. True only when the process
/// is interactive (<see cref="Environment.UserInteractive"/>) and neither stdout nor stdin is redirected. A Windows
/// Service, a Docker/headless container, a CI run, and any piped/automation launch (how the boot is verified in
/// automation) all redirect or run non-interactively, so this is false there — no dialog, no tray.
/// </summary>
public static class InteractiveDesktopGuard
{
    /// <summary>True when surfacing user-facing desktop UI is appropriate (interactive, non-redirected session).</summary>
    public static bool IsInteractiveDesktopSession =>
        IsInteractiveDesktop(
            Environment.UserInteractive,
            Console.IsOutputRedirected,
            Console.IsInputRedirected
        );

    /// <summary>
    /// The pure decision, with the environment signals passed in so it is testable without the real host. A human
    /// desktop session requires an interactive process AND a console that is not redirected on either stream.
    /// </summary>
    public static bool IsInteractiveDesktop(
        bool userInteractive,
        bool outputRedirected,
        bool inputRedirected
    ) => userInteractive && !outputRedirected && !inputRedirected;
}
