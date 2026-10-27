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
using System.Reflection;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

/// <summary>The real process: its start time and the informational version of the build.</summary>
internal sealed class RuntimeBotProcessInfo : IBotProcessInfo
{
    private const string Unknown = "0.0.0";

    public DateTimeOffset StartedAt { get; } = ReadStart();

    public string Version { get; } = ReadVersion();

    private static DateTimeOffset ReadStart()
    {
        using Process process = Process.GetCurrentProcess();
        return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
    }

    /// <summary>The informational version carries <c>+&lt;commit&gt;</c> after the number; the number is the version.</summary>
    private static string ReadVersion()
    {
        Assembly assembly = typeof(RuntimeBotProcessInfo).Assembly;
        string version =
            assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? Unknown;
        int plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }
}
