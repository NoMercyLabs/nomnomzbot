// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces;

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// Gives the windowless self-host single binary (<c>WinExe</c> — no console, no main window) a Windows system-tray
/// icon: a visible "it's running" indicator with a right/left-click menu to open the dashboard or stop the bot. It
/// runs a private Win32 message loop on a dedicated background thread (a message-only window + <c>Shell_NotifyIcon</c>),
/// so it never touches the request pipeline. Best-effort UX exactly like <see cref="StartupErrorNotifier"/>: any
/// failure is logged and swallowed — the tray must never destabilise the host.
/// <para>
/// Registered only when <see cref="SystemTrayGate"/> passes (self-host profile, Windows, interactive desktop), so
/// this service is constructed solely on Windows; all P/Invoke is additionally guarded by
/// <see cref="OperatingSystem.IsWindows()"/> for the analyzer and for defence in depth.
/// </para>
/// </summary>
public sealed partial class SystemTrayHostedService : IHostedService
{
    private static readonly TimeSpan StopJoinTimeout = TimeSpan.FromSeconds(5);

    private readonly IListenEndpointAccessor _endpoint;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SystemTrayHostedService> _logger;

    private Thread? _uiThread;
    private uint _uiThreadId;

    // The window procedure must be kept alive for the window's lifetime so the GC never collects the thunk the
    // OS holds a pointer to. Held in a field, assigned once on the UI thread.
    private WndProc? _wndProc;
    private IntPtr _hwnd;
    private IntPtr _menuIcon;
    private bool _iconAdded;

    public SystemTrayHostedService(
        IListenEndpointAccessor endpoint,
        IHostApplicationLifetime lifetime,
        ILogger<SystemTrayHostedService> logger
    )
    {
        _endpoint = endpoint;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return Task.CompletedTask;

        try
        {
            // A dedicated STA-style background thread owns the message-only window and pumps the loop until
            // WM_QUIT. It is background so it can never keep the process alive past host shutdown.
            _uiThread = new Thread(RunMessageLoop) { IsBackground = true, Name = "nomnomz-tray" };
            _uiThread.Start();
        }
        catch (Exception ex)
        {
            // Tray is convenience-only; a failed start must not abort the host.
            _logger.LogWarning(ex, "System-tray icon could not start; continuing without it.");
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            // Ask the UI thread's loop to exit (it removes the icon, then PostQuitMessage breaks GetMessage).
            if (_uiThreadId != 0)
                PostThreadMessageW(_uiThreadId, WM_APP_SHUTDOWN, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "System-tray shutdown signal could not be posted.");
        }

        // Join the UI thread, bounded, so a wedged loop can never hang host shutdown.
        Thread? thread = _uiThread;
        if (thread is not null)
            await Task.Run(
                () =>
                {
                    if (!thread.Join(StopJoinTimeout))
                        _logger.LogDebug(
                            "System-tray thread did not exit within the join timeout."
                        );
                },
                CancellationToken.None
            );
    }

    // ── UI thread: window + message loop ─────────────────────────────────────────────────────

    private void RunMessageLoop()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            _uiThreadId = GetCurrentThreadId();
            CreateMessageWindow();
            AddTrayIcon();
            PumpMessages();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "System-tray message loop failed; the tray icon is unavailable."
            );
        }
        finally
        {
            TeardownTray();
        }
    }

    private void CreateMessageWindow()
    {
        _wndProc = WindowProc;

        WNDCLASSEXW windowClass = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            // CS_DBLCLKS so the tray's message window receives WM_LBUTTONDBLCLK — without it Windows never
            // delivers double-click messages, so double-click-to-open would silently never fire.
            style = CS_DBLCLKS,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = WindowClassName,
        };

        ushort atom = RegisterClassExW(ref windowClass);
        if (atom == 0)
            throw new InvalidOperationException(
                $"RegisterClassEx failed (0x{Marshal.GetLastPInvokeError():X8})."
            );

        // HWND_MESSAGE makes this a message-only window: no taskbar entry, no visible surface — it exists purely
        // to receive the tray callback and menu-command messages.
        _hwnd = CreateWindowExW(
            0,
            WindowClassName,
            "NomNomzBot",
            0,
            0,
            0,
            0,
            0,
            HWND_MESSAGE,
            IntPtr.Zero,
            windowClass.hInstance,
            IntPtr.Zero
        );

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                $"CreateWindowEx failed (0x{Marshal.GetLastPInvokeError():X8})."
            );
    }

    private void AddTrayIcon()
    {
        int port = TryGetPort();
        _menuIcon = LoadApplicationIcon();

        NOTIFYICONDATAW data = NewIconData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_TRAY_CALLBACK;
        data.hIcon = _menuIcon;
        data.szTip = SystemTrayMenu.Tooltip(port);

        _iconAdded = Shell_NotifyIconW(NIM_ADD, ref data);
        if (!_iconAdded)
        {
            _logger.LogWarning("Shell_NotifyIcon(NIM_ADD) failed; the tray icon is unavailable.");
            return;
        }

        // Minimal startup balloon: a one-shot "running" cue. Best-effort — a failure here is ignored.
        NOTIFYICONDATAW balloon = NewIconData();
        balloon.uFlags = NIF_INFO;
        balloon.szInfoTitle = "NomNomzBot";
        balloon.szInfo = $"NomNomzBot is running on {SystemTrayMenu.DashboardUrl(port)}";
        balloon.dwInfoFlags = NIIF_INFO;
        _ = Shell_NotifyIconW(NIM_MODIFY, ref balloon);
    }

    private void PumpMessages()
    {
        // Standard Win32 loop. GetMessage returns 0 on WM_QUIT and -1 on error; either ends the loop so the thread
        // can tear down and exit. The stop signal arrives as a THREAD message (PostThreadMessage, hwnd == 0), which
        // DispatchMessage never routes to a window procedure — so it must be recognised here in the loop, not in
        // WindowProc. Breaking falls into RunMessageLoop's finally, which removes the icon and destroys the window.
        while (true)
        {
            int result = GetMessageW(out MSG message, IntPtr.Zero, 0, 0);
            if (result is 0 or -1)
                break;
            if (message.hwnd == IntPtr.Zero && message.message == WM_APP_SHUTDOWN)
                break;
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TRAY_CALLBACK:
                // The mouse event arrives in the low word of lParam. Double-click opens the dashboard (the
                // primary action); a right-click — or the keyboard context-menu key — raises the menu, which
                // also lists "Open dashboard". A single left-click is intentionally a no-op so it can't fire
                // mid double-click.
                uint mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouseMessage == WM_LBUTTONDBLCLK)
                    OpenDesktopApp();
                else if (mouseMessage is WM_RBUTTONUP or WM_CONTEXTMENU)
                    ShowContextMenu(hwnd);
                return IntPtr.Zero;

            case WM_COMMAND:
                uint commandId = (uint)(wParam.ToInt64() & 0xFFFF);
                HandleCommand(commandId);
                return IntPtr.Zero;

            default:
                return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private void ShowContextMenu(IntPtr hwnd)
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            // Disabled header (greyed) + separator, then the actionable items modelled in SystemTrayMenu.
            _ = AppendMenuW(menu, MF_STRING | MF_GRAYED | MF_DISABLED, IntPtr.Zero, "NomNomzBot");
            _ = AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);

            foreach (TrayMenuItem item in SystemTrayMenu.Items)
            {
                if (item.Command == TrayCommand.StopApplication)
                    _ = AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);
                _ = AppendMenuW(menu, MF_STRING, (IntPtr)item.CommandId, item.Label);
            }

            // TrackPopupMenu requires the owning window to be foreground, else the menu won't dismiss on click-away.
            _ = SetForegroundWindow(hwnd);
            if (!GetCursorPos(out POINT cursor))
                return;

            // Synchronous: returns the chosen command id (0 if dismissed). TPM_RETURNCMD routes it here rather than
            // posting WM_COMMAND, but we also handle WM_COMMAND for completeness; act on the returned id directly.
            uint chosen = (uint)TrackPopupMenuEx(
                menu,
                TPM_RIGHTBUTTON | TPM_RETURNCMD,
                cursor.X,
                cursor.Y,
                hwnd,
                IntPtr.Zero
            );
            if (chosen != 0)
                HandleCommand(chosen);
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private void HandleCommand(uint commandId)
    {
        TrayCommand? command = SystemTrayMenu.ResolveCommand(commandId);
        switch (command)
        {
            case TrayCommand.OpenApp:
                OpenDesktopApp();
                break;
            case TrayCommand.OpenDashboard:
                OpenDashboard();
                break;
            case TrayCommand.StopApplication:
                _logger.LogInformation(
                    "System-tray: stopping NomNomzBot at the operator's request."
                );
                _lifetime.StopApplication();
                break;
        }
    }

    private void OpenDesktopApp()
    {
        // The self-host single-exe bundles the native desktop app under desktop\ next to the bot exe; launch it.
        // When it isn't present (e.g. a bare Docker image with no GUI), degrade to opening the web dashboard so a
        // double-click always does something useful.
        string? botDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
        string? appPath = botDir is null
            ? null
            : System.IO.Path.Combine(
                botDir,
                SystemTrayMenu.DesktopAppFolder,
                SystemTrayMenu.DesktopAppExeName
            );

        if (appPath is null || !System.IO.File.Exists(appPath))
        {
            _logger.LogInformation(
                "System-tray: bundled desktop app not found at {AppPath}; opening the web dashboard instead.",
                appPath
            );
            OpenDashboard();
            return;
        }

        try
        {
            // UseShellExecute launches the packaged exe; its own folder is the working dir so it finds its runtime.
            Process.Start(
                new ProcessStartInfo(appPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(appPath),
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "System-tray: could not launch the desktop app {AppPath}; opening the web dashboard.",
                appPath
            );
            OpenDashboard();
        }
    }

    private void OpenDashboard()
    {
        string url = SystemTrayMenu.DashboardUrl(TryGetPort());
        try
        {
            // UseShellExecute launches the default browser for the URL.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "System-tray: could not open the dashboard URL {Url}.", url);
        }
    }

    // ── Teardown ─────────────────────────────────────────────────────────────────────────────

    private void TeardownTray()
    {
        RemoveTrayIcon();

        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                _ = DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            _ = UnregisterClassW(WindowClassName, GetModuleHandleW(null));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "System-tray window teardown was not clean.");
        }

        if (_menuIcon != IntPtr.Zero)
        {
            _ = DestroyIcon(_menuIcon);
            _menuIcon = IntPtr.Zero;
        }
    }

    private void RemoveTrayIcon()
    {
        if (!_iconAdded)
            return;
        try
        {
            NOTIFYICONDATAW data = NewIconData();
            _ = Shell_NotifyIconW(NIM_DELETE, ref data);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "System-tray icon removal was not clean.");
        }
        finally
        {
            _iconAdded = false;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private NOTIFYICONDATAW NewIconData() =>
        new()
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = TrayIconId,
        };

    private int TryGetPort()
    {
        // The accessor throws if read before the port is published; fall back to the conventional default so the
        // URL/tooltip is still sensible rather than crashing the tray thread.
        try
        {
            return _endpoint.IsResolved ? _endpoint.Port : DefaultPort;
        }
        catch
        {
            return DefaultPort;
        }
    }

    private IntPtr LoadApplicationIcon()
    {
        // Prefer the running exe's own icon so the tray matches the app; fall back to the generic application icon.
        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                IntPtr extracted = ExtractIconW(GetModuleHandleW(null), exePath, 0);
                // ExtractIcon returns 1 ("no icons in file") or 0 (error) when there is no usable icon.
                if (extracted != IntPtr.Zero && extracted != (IntPtr)1)
                    return extracted;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "System-tray: extracting the exe icon failed; using the default icon."
            );
        }

        return LoadIconW(IntPtr.Zero, IDI_APPLICATION);
    }

    // ── Win32 constants ──────────────────────────────────────────────────────────────────────

    private const string WindowClassName = "NomNomzBotTrayWindow";
    private const uint TrayIconId = 1;
    private const int DefaultPort = 5080;

    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static readonly IntPtr IDI_APPLICATION = new(32512);

    private const uint WM_APP = 0x8000;
    private const uint WM_TRAY_CALLBACK = WM_APP + 1;
    private const uint WM_APP_SHUTDOWN = WM_APP + 2;

    private const uint WM_COMMAND = 0x0111;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_LBUTTONDBLCLK = 0x0203;

    // CS_DBLCLKS: ask Windows to deliver double-click messages to this window class.
    private const uint CS_DBLCLKS = 0x0008;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIIF_INFO = 0x00000001;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_DISABLED = 0x00000002;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    // ── Win32 interop ────────────────────────────────────────────────────────────────────────

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszClassName;

        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersionOrTimeout;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessageW(
        out MSG lpMsg,
        IntPtr hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(
        uint idThread,
        uint msg,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(
        IntPtr hMenu,
        uint uFlags,
        IntPtr uIDNewItem,
        string? lpNewItem
    );

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(
        IntPtr hInst,
        string lpszExeFileName,
        uint nIconIndex
    );
}
