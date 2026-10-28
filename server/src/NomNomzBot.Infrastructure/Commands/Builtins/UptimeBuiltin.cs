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

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// !uptime — reports how long the stream has been live. Computes the REAL elapsed time from the channel
/// registry's <c>WentLiveAt</c> and renders it in the channel's personality tone (override → tone → neutral).
/// </summary>
public sealed class UptimeBuiltin : IBuiltinCommand
{
    private readonly IChannelRegistry _registry;
    private readonly IBuiltinResponseComposer _composer;
    private readonly TimeProvider _clock;

    public UptimeBuiltin(
        IChannelRegistry registry,
        IBuiltinResponseComposer composer,
        TimeProvider clock
    )
    {
        _registry = registry;
        _composer = composer;
        _clock = clock;
    }

    public string BuiltinKey => BuiltinResponseSlots.Uptime.Key;
    public int DefaultCooldownSeconds => 30;
    public int DefaultMinPermissionLevel => 0;

    /// <summary>Read-only for any viewer, so a whisper and <c>{include.builtin:uptime}</c> may run it.</summary>
    public bool WhisperSafe => true;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        ChannelContext? ctx = _registry.Get(context.BroadcasterId);

        if (ctx is null || !ctx.IsLive)
        {
            string offline = await _composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinKey,
                    Slot = BuiltinResponseSlots.Uptime.Offline,
                    NeutralFallback = "The stream is currently offline.",
                },
                ct
            );
            return Result.Success(offline);
        }

        string uptime = FormatUptime(_clock.GetUtcNow() - ctx.WentLiveAt);

        string live = await _composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = BuiltinResponseSlots.Uptime.Live,
                // The override customizes the primary (live) response; the offline state stays tone-driven.
                NeutralFallback = "The stream has been live for {uptime}.",
                Variables = new Dictionary<string, string> { ["uptime"] = uptime },
            },
            ct
        );
        return Result.Success(live);
    }

    /// <summary>Compact H/M/S uptime; a null/negative span (missing WentLiveAt) degrades to "some time".</summary>
    private static string FormatUptime(TimeSpan? span)
    {
        if (span is not { } uptime || uptime <= TimeSpan.Zero)
            return "some time";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        if (uptime.TotalMinutes >= 1)
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        return $"{uptime.Seconds}s";
    }
}
