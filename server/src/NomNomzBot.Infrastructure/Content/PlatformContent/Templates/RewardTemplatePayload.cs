// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// The <c>reward</c> template payload — the portable definition of a channel-point <see cref="Reward"/>. No
/// Twitch id and no pipeline id: install creates the reward on the installing channel's Twitch, and a pipeline
/// to run on redemption is one of that channel's own pipelines, picked at install time.
/// </summary>
public sealed record RewardTemplatePayload
{
    public string Title { get; init; } = string.Empty;
    public int Cost { get; init; }
    public string? Prompt { get; init; }
    public string? Response { get; init; }
    public bool IsUserInputRequired { get; init; }
    public string? BackgroundColor { get; init; }
    public int? MaxPerStream { get; init; }
    public int? MaxPerUserPerStream { get; init; }
    public int? GlobalCooldownSeconds { get; init; }
    public int? TimerDurationSeconds { get; init; }

    public static RewardTemplatePayload FromEntity(Reward row) =>
        new()
        {
            Title = row.Title,
            Cost = row.Cost ?? 0,
            Prompt = string.IsNullOrEmpty(row.Description) ? null : row.Description,
            Response = string.IsNullOrEmpty(row.Response) ? null : row.Response,
            IsUserInputRequired = row.IsUserInputRequired,
            BackgroundColor = row.BackgroundColor,
            MaxPerStream = row.MaxPerStream,
            MaxPerUserPerStream = row.MaxPerUserPerStream,
            GlobalCooldownSeconds = row.GlobalCooldownSeconds,
            TimerDurationSeconds = row.TimerDurationSeconds,
        };

    public string ComputeHash() => PlatformTemplateJson.Hash(this);
}
