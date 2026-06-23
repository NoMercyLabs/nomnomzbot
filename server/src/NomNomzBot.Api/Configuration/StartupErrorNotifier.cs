// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.InteropServices;

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// Surfaces a fatal startup failure to a human running the self-host single binary. That binary is windowless
/// (WinExe — there is no console to print to or hold open), so a fatal error is shown in a Windows dialog instead
/// of vanishing. No-op for services, Docker/CI, and redirected/automation runs — there the fatal lives in the log.
/// </summary>
internal static class StartupErrorNotifier
{
    public static void Notify(Exception ex)
    {
        // Only a human at an interactive desktop session should be interrupted — never a service, container, CI,
        // or a redirected/piped launch (which is how the boot is verified in automation).
        if (!Environment.UserInteractive || Console.IsOutputRedirected || Console.IsInputRedirected)
            return;

        string message =
            $"NomNomzBot could not start:\n\n{ex.Message}\n\n"
            + "See the log files in the NomNomzBot data folder for details.";

        if (OperatingSystem.IsWindows())
        {
            const uint MB_ICONERROR = 0x00000010;
            const uint MB_SETFOREGROUND = 0x00010000;
            _ = MessageBoxW(
                IntPtr.Zero,
                message,
                "NomNomzBot — startup failed",
                MB_ICONERROR | MB_SETFOREGROUND
            );
            return;
        }

        // A non-Windows interactive terminal still has a console — print and hold so it doesn't scroll away.
        Console.Error.WriteLine();
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Press any key to close...");
        Console.ReadKey(intercept: true);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
