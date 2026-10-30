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

using System.Text.Json.Serialization;

namespace NomNomzBot.Domain.Commands.Enums;

/// <summary>
/// What kind of failure ended a pipeline run. Set at the place the failure happens, never read back out of
/// the error text. A run that succeeded has no kind. <see cref="Limit"/> and <see cref="Timeout"/> come with
/// the limit that stopped the run (a key and a value) on the run row.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunFailureKind
{
    /// <summary>A count or size limit stopped the run (host calls, statements, memory, loop passes, quota).</summary>
    Limit,

    /// <summary>A time budget ran out (the script's own run time, or the whole pipeline's).</summary>
    Timeout,

    /// <summary>A host call threw instead of answering with a refusal.</summary>
    HostCallError,

    /// <summary>The script itself failed (a thrown error or a fault inside the sandbox).</summary>
    ScriptError,

    /// <summary>The run was not allowed to do what it asked (capability, plan or switch).</summary>
    Permission,

    /// <summary>A call to a platform API (Twitch, Discord, ...) failed.</summary>
    PlatformApiError,

    /// <summary>The run was canceled from outside (the channel went offline or the caller stopped it).</summary>
    Canceled,
}
