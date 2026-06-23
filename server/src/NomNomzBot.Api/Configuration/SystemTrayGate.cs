// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Enums.Deployment;

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// Decides whether the windowless self-host single binary should show a Windows system-tray icon. The tray is a
/// self-host desktop convenience, so it is shown only when ALL hold: the resolved deployment mode is a self-host
/// profile (off for SaaS), the OS is Windows (the tray is Win32-only), and the process runs in an interactive
/// session (<see cref="Environment.UserInteractive"/> — off for a Windows Service / session-0 / headless container).
/// <para>
/// Interactivity is gauged by <see cref="Environment.UserInteractive"/> and <b>deliberately not</b> by the
/// console-redirection probe <see cref="InteractiveDesktopGuard"/> uses for the blocking startup dialog: this is a
/// windowless <c>WinExe</c> with <b>no console</b>, so <c>Console.IsOutputRedirected</c> is <c>true</c> even on a
/// normal desktop double-click — that probe would suppress the tray for the exact user it exists for. The tray is a
/// non-blocking icon, so it is safe to show whenever the session is interactive (a stray icon in CI/automation is
/// harmless), unlike a modal dialog that must never appear where nothing can dismiss it. The OS + interactive
/// signals are passed in so the truth table is testable without the real host.
/// </para>
/// </summary>
public static class SystemTrayGate
{
    /// <summary>Evaluates the gate using the live OS + interactive-session signals.</summary>
    public static bool ShouldShowTray(DeploymentMode mode) =>
        ShouldShowTray(mode, OperatingSystem.IsWindows(), Environment.UserInteractive);

    /// <summary>
    /// The pure decision. Tray shows iff a self-host profile AND Windows AND an interactive session. Any single
    /// false (SaaS, non-Windows, or a non-interactive service/headless session) suppresses it.
    /// </summary>
    public static bool ShouldShowTray(
        DeploymentMode mode,
        bool isWindows,
        bool isUserInteractive
    ) => IsSelfHost(mode) && isWindows && isUserInteractive;

    private static bool IsSelfHost(DeploymentMode mode) =>
        mode is DeploymentMode.SelfHostLite or DeploymentMode.SelfHostFull;
}
