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

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>How an <c>activity.*</c> row reads its source.</summary>
internal static class ActivityRows
{
    /// <summary>A row over the in-memory ring of the last hour of chat.</summary>
    public static BroadcastRow<ActivityData> OverRing(
        string key,
        string sample,
        Func<BroadcastCall<ActivityData>, string?> print,
        int minArgs = 0,
        int maxArgs = 0
    ) =>
        new(
            key,
            sample,
            "chatRing",
            TemplateHelperFreshness.Memory,
            minArgs,
            maxArgs,
            call => Task.FromResult(print(call))
        );

    /// <summary>A row over the channel's live state (the session chatters, the last message, the command count).</summary>
    public static BroadcastRow<ActivityData> OverChannel(
        string key,
        string sample,
        Func<ChannelContext, BroadcastCall<ActivityData>, string?> print
    ) =>
        OverRing(
            key,
            sample,
            call => call.Scope.Channel is { } channel ? print(channel, call) : null
        ) with
        {
            Source = "channel",
        };

    /// <summary>A row over a count fetched from an outside source; no count prints the token as written.</summary>
    public static BroadcastRow<ActivityData> OverCount(
        string key,
        string sample,
        string source,
        Func<BroadcastCall<ActivityData>, Task<long?>> read,
        Func<long, BroadcastCall<ActivityData>, string?> print
    ) =>
        new(
            key,
            sample,
            source,
            TemplateHelperFreshness.Cached,
            0,
            0,
            async call => await read(call) is { } count ? print(count, call) : null
        );

    /// <summary>A row over a value fetched from an outside source; no value (the source is down) prints the token as written.</summary>
    public static BroadcastRow<ActivityData> OverSource<TValue>(
        string key,
        string sample,
        string source,
        Func<BroadcastCall<ActivityData>, Task<TValue?>> read,
        Func<TValue, BroadcastCall<ActivityData>, string?> print,
        int args = 0
    ) =>
        new(
            key,
            sample,
            source,
            TemplateHelperFreshness.Cached,
            args,
            args,
            async call => await read(call) is { } value ? print(value, call) : null
        );
}
