// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Stream.PipelineActions;

/// <summary>
/// Waits for whatever time remains until the Twitch raid started by a prior <c>start_raid</c> step
/// actually auto-fires (that step stamps <c>raid.fires_at_utc_ticks</c> into
/// <see cref="PipelineExecutionContext.Variables"/> the instant the raid call returns).
///
/// This exists because a chain of fixed <c>wait</c> steps only adds up correctly if nothing between
/// <c>start_raid</c> and here ever runs slow — an OBS scene switch or a chat send that takes an extra
/// second silently pushes the whole countdown late relative to Twitch's own 90s server-side timer. This
/// step re-anchors to the ACTUAL deadline instead of trusting the accumulated total, absorbing whatever
/// drift built up in between (matches the legacy bot's own <c>twitchFireAt</c> wall-clock wait).
///
/// A no-op (immediate success) when no prior <c>start_raid</c> ran in this execution, or when the
/// deadline has already passed — never a failure, since the raid itself already happened either way.
/// Capped at <see cref="StartRaidAction.TwitchRaidWindowSeconds"/> so a corrupt/stale variable can never
/// hang the pipeline.
///
/// Usage example:
///   { "type": "wait_until_raid_fires" }
/// </summary>
public sealed class WaitUntilRaidFiresAction : ICommandAction
{
    public string ActionType => "wait_until_raid_fires";

    public LocalizedText Category => new("pipeline.category.stream");

    public LocalizedText Description => new("pipeline.wait_until_raid_fires.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields => [];

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        TimeSpan? wait = ComputeWait(ctx.Variables, DateTime.UtcNow);

        if (wait is null)
            return ActionResult.Success(
                "no start_raid deadline recorded in this run — nothing to wait for"
            );

        if (wait.Value <= TimeSpan.Zero)
            return ActionResult.Success("raid deadline already passed — nothing left to wait for");

        await Task.Delay(wait.Value, ctx.CancellationToken);
        return ActionResult.Success("waited out the remaining time to the raid's auto-fire");
    }

    /// <summary>
    /// Pure clock math, split out so the capping/negative/missing-variable behavior is directly
    /// testable without a unit test having to actually sleep through a bogus far-future deadline.
    /// <c>null</c> means "no deadline recorded" (missing/unparsable variable); otherwise the exact
    /// duration to wait, already clamped to <see cref="StartRaidAction.TwitchRaidWindowSeconds"/> and
    /// never negative.
    /// </summary>
    internal static TimeSpan? ComputeWait(
        IReadOnlyDictionary<string, string> variables,
        DateTime utcNow
    )
    {
        if (
            !variables.TryGetValue("raid.fires_at_utc_ticks", out string? rawTicks)
            || !long.TryParse(rawTicks, out long ticks)
        )
            return null;

        DateTime firesAt = new(ticks, DateTimeKind.Utc);
        TimeSpan remaining = firesAt - utcNow;

        if (remaining <= TimeSpan.Zero)
            return TimeSpan.Zero;

        TimeSpan cap = TimeSpan.FromSeconds(StartRaidAction.TwitchRaidWindowSeconds);
        return remaining > cap ? cap : remaining;
    }
}
