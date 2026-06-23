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
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// The real OS implementation of <see cref="IPortOperations"/> (deployment-distribution §6).
/// <list type="bullet">
///   <item>Bindability + ephemeral allocation use a loopback <see cref="TcpListener"/> (the same mechanism Kestrel
///   uses), so "bindable" here means "Kestrel can bind it".</item>
///   <item>The owning PID comes from the Windows TCP table via <c>GetExtendedTcpTable</c> (iphlpapi). On non-Windows
///   the PID can't be obtained portably without parsing OS-specific tables, so it returns <c>null</c> — which the
///   resolver reads as "another app, step aside" (it never kills on an unknown owner).</item>
/// </list>
/// </summary>
public sealed class SystemPortOperations : IPortOperations
{
    public bool IsPortBindable(int port)
    {
        try
        {
            TcpListener listener = new(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public int FindFreeEphemeralPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public int? GetListeningProcessId(int port)
    {
        if (!OperatingSystem.IsWindows())
            return null; // Best-effort: no portable PID lookup off Windows → resolver treats the owner as "another app".

        return WindowsTcpTable.FindListenerPid(port);
    }

    public string? GetProcessName(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.ProcessName; // already extension-less, e.g. "nomnomz"
        }
        catch (ArgumentException)
        {
            return null; // process is gone
        }
        catch (InvalidOperationException)
        {
            return null; // process has exited between lookup and read
        }
    }

    public string CurrentProcessName
    {
        get
        {
            using Process self = Process.GetCurrentProcess();
            return self.ProcessName;
        }
    }

    public bool KillProcessAndWait(int pid, TimeSpan timeout)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(timeout);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true; // already gone
        }
        catch (InvalidOperationException)
        {
            return true; // already exited
        }
        catch (Exception)
        {
            return false; // access denied / could not kill — caller falls back to an ephemeral port
        }
    }

    /// <summary>
    /// Minimal P/Invoke over <c>iphlpapi!GetExtendedTcpTable</c> to find the PID of the IPv4 process LISTENING on a
    /// given local port. Scoped to exactly what the resolver needs; never throws (any failure → <c>null</c>).
    /// </summary>
    private static class WindowsTcpTable
    {
        private const int AfInet = 2; // AF_INET
        private const int TcpStateListen = 2; // MIB_TCP_STATE_LISTEN

        // TCP_TABLE_OWNER_PID_LISTENER — only the listening sockets, keyed by owning PID.
        private const int TcpTableOwnerPidListener = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcpRowOwnerPid
        {
            public uint State;
            public uint LocalAddr;
            public uint LocalPort; // network byte order, low word
            public uint RemoteAddr;
            public uint RemotePort;
            public uint OwningPid;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            int tableClass,
            int reserved
        );

        public static int? FindListenerPid(int port)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int size = 0;
                // First call sizes the buffer.
                uint result = GetExtendedTcpTable(
                    IntPtr.Zero,
                    ref size,
                    false,
                    AfInet,
                    TcpTableOwnerPidListener,
                    0
                );
                const uint errorInsufficientBuffer = 122;
                if (result != errorInsufficientBuffer && result != 0)
                    return null;

                buffer = Marshal.AllocHGlobal(size);
                result = GetExtendedTcpTable(
                    buffer,
                    ref size,
                    false,
                    AfInet,
                    TcpTableOwnerPidListener,
                    0
                );
                if (result != 0)
                    return null;

                int rowCount = Marshal.ReadInt32(buffer); // leading dwNumEntries
                IntPtr rowPtr = buffer + sizeof(int);
                int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

                for (int i = 0; i < rowCount; i++)
                {
                    MibTcpRowOwnerPid row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                    rowPtr += rowSize;

                    if (row.State != TcpStateListen)
                        continue;

                    // LocalPort is the low 16 bits in network byte order: swap the two bytes to host order.
                    int rowPort =
                        ((int)(row.LocalPort & 0xFF) << 8) | (int)((row.LocalPort >> 8) & 0xFF);
                    if (rowPort == port)
                        return (int)row.OwningPid;
                }

                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
