// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The row factories of the <c>hypetrain</c> namespace.</summary>
internal static class LiveHypeTrainRows
{
    private const string Ns = "hypetrain";

    /// <summary>A row over the train that runs now; it prints nothing when no train runs.</summary>
    public static LiveEventRow Train(
        string key,
        string sample,
        Func<TwitchHypeTrain, LiveEventCall, string?> print
    ) =>
        Row(
            key,
            sample,
            async call =>
                call.Args.Count == 0 && await Data(call).CurrentAsync() is { } train
                    ? print(train, call)
                    : null
        );

    /// <summary>A row over the all-time record; it prints nothing when Twitch has no record yet.</summary>
    public static LiveEventRow Record(
        string key,
        string sample,
        Func<TwitchHypeTrainRecord, string?> print
    ) =>
        Row(
            key,
            sample,
            async call =>
                call.Args.Count == 0
                && await Data(call).StatusAsync() is { AllTimeHigh: { } record }
                    ? print(record)
                    : null
        );

    /// <summary>A yes-or-no row for <c>{if:...}</c>; "false" when no train runs.</summary>
    public static LiveEventRow Flag(string key, Func<TwitchHypeTrain, bool> test) =>
        Row(
            key,
            "false",
            async call =>
                call.Args.Count == 0
                    ? LiveText.Flag(await Data(call).CurrentAsync() is { } train && test(train))
                    : null
        );

    private static LiveEventRow Row(
        string key,
        string sample,
        Func<LiveEventCall, Task<string?>> render
    ) => new(key, sample, Ns, data => ((LiveHypeTrainData)data).StatusAsync(), render);

    private static LiveHypeTrainData Data(LiveEventCall call) => (LiveHypeTrainData)call.Data;
}
