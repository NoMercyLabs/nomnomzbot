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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>How a <c>schedule.*</c> row reads the schedule.</summary>
internal static class ScheduleRows
{
    private const string Source = "schedule";

    /// <summary>A row over the schedule view; no schedule (Twitch is down) prints the token as written.</summary>
    public static BroadcastRow<ScheduleData> Over(
        string key,
        string sample,
        Func<ScheduleView, BroadcastCall<ScheduleData>, string?> print,
        int args = 0
    ) =>
        new(
            key,
            sample,
            Source,
            TemplateHelperFreshness.Cached,
            args,
            args,
            async call =>
                await call.Data.ViewAsync(call.Now, call.Ct) is { } view ? print(view, call) : null
        );

    /// <summary>A row over the next segment; no segment ahead prints the token as written.</summary>
    public static BroadcastRow<ScheduleData> OverNext(
        string key,
        string sample,
        Func<TwitchScheduleSegment, BroadcastCall<ScheduleData>, string?> print,
        int args = 0
    ) =>
        Over(
            key,
            sample,
            (view, call) => view.Upcoming.Count > 0 ? print(view.Upcoming[0], call) : null,
            args
        );

    /// <summary>A row that needs no Twitch call: a pure value of the channel.</summary>
    public static BroadcastRow<ScheduleData> OfChannel(
        string key,
        string sample,
        Func<ChannelContext, string?> print
    ) =>
        BroadcastRow<ScheduleData>.Of(
            key,
            sample,
            Source,
            TemplateHelperFreshness.Memory,
            call => Task.FromResult(call.Scope.Channel is { } channel ? print(channel) : null)
        );
}
