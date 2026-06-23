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
/// The action a tray menu item triggers — modelled as data so the Win32 <c>WM_COMMAND</c> handler is a pure lookup
/// (command id → action) rather than a hand-rolled switch, and so the mapping is unit-testable without any P/Invoke.
/// </summary>
public enum TrayCommand
{
    /// <summary>Launch the bundled native desktop app — the primary action (double-click).</summary>
    OpenApp,

    /// <summary>Open the local dashboard URL in the default browser.</summary>
    OpenDashboard,

    /// <summary>Gracefully stop the bot via the host application lifetime.</summary>
    StopApplication,
}

/// <summary>
/// One clickable tray menu item: the Win32 command id raised in <c>WM_COMMAND</c>, the label shown, and the action
/// it maps to. The header and separator are not items here — only the actionable entries carry a command id.
/// </summary>
/// <param name="CommandId">The <c>uIDNewItem</c> passed to <c>AppendMenu</c>; echoed back in <c>WM_COMMAND</c>'s wParam.</param>
/// <param name="Label">The menu text.</param>
/// <param name="Command">The action this item triggers.</param>
public readonly record struct TrayMenuItem(uint CommandId, string Label, TrayCommand Command);

/// <summary>
/// The pure, P/Invoke-free model of the tray's dashboard URL, tooltip, and menu items, all derived from the bound
/// listen port. The hosted service builds the Win32 menu from <see cref="Items"/> and resolves a clicked command id
/// back to its <see cref="TrayCommand"/> via <see cref="ResolveCommand"/>; everything here is deterministic and
/// testable so the only untested surface is the message loop itself.
/// </summary>
public static class SystemTrayMenu
{
    /// <summary>Win32 command id for "Open dashboard". Any non-zero id works; these are stable and distinct.</summary>
    public const uint OpenDashboardCommandId = 0x1001;

    /// <summary>Win32 command id for "Stop NomNomzBot".</summary>
    public const uint StopCommandId = 0x1002;

    /// <summary>Win32 command id for "Open NomNomzBot" (the bundled desktop app).</summary>
    public const uint OpenAppCommandId = 0x1003;

    /// <summary>
    /// The bundled desktop app's location relative to the bot executable: the self-host single-exe ships the
    /// native desktop app under <c>desktop\</c> next to <c>nomnomz.exe</c>, so the tray launches
    /// <c>&lt;botDir&gt;\desktop\NomNomzBot.exe</c>. Absent (e.g. the bare Docker image) ⇒ the tray falls back to the
    /// web dashboard.
    /// </summary>
    public const string DesktopAppFolder = "desktop";

    /// <summary>The bundled desktop app's executable name (matches the Compose <c>packageName</c>).</summary>
    public const string DesktopAppExeName = "NomNomzBot.exe";

    /// <summary>The local dashboard URL for the bound port, e.g. <c>http://localhost:5080</c>.</summary>
    public static string DashboardUrl(int port) => $"http://localhost:{port}";

    /// <summary>The tray tooltip, e.g. <c>NomNomzBot — running on http://localhost:5080</c>.</summary>
    public static string Tooltip(int port) => $"NomNomzBot — running on {DashboardUrl(port)}";

    /// <summary>The actionable menu items, in display order. The header + separator are added by the renderer.</summary>
    public static IReadOnlyList<TrayMenuItem> Items { get; } =
    [
        new(OpenAppCommandId, "Open NomNomzBot", TrayCommand.OpenApp),
        new(OpenDashboardCommandId, "Open in browser", TrayCommand.OpenDashboard),
        new(StopCommandId, "Stop NomNomzBot", TrayCommand.StopApplication),
    ];

    /// <summary>
    /// Maps a Win32 <c>WM_COMMAND</c> command id back to its action, or <c>null</c> for an unknown id (which the
    /// message loop then ignores). Pure lookup over <see cref="Items"/> — no switch to drift out of sync.
    /// </summary>
    public static TrayCommand? ResolveCommand(uint commandId)
    {
        foreach (TrayMenuItem item in Items)
            if (item.CommandId == commandId)
                return item.Command;
        return null;
    }
}
