// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// The <c>timer</c> template payload — the portable part of a <see cref="DomainTimer"/> row. A timer with no
/// messages runs a pipeline, which the installing channel picks from its own pipelines at install time.
/// </summary>
public sealed record TimerTemplatePayload
{
    public string Name { get; init; } = string.Empty;
    public List<string> Messages { get; init; } = [];
    public int IntervalMinutes { get; init; } = 30;
    public int MinChatActivity { get; init; }
    public bool FireOnce { get; init; }
    public bool IsEnabled { get; init; } = true;

    /// <summary>True when the template has no messages, so the timer only runs a pipeline.</summary>
    [JsonIgnore]
    public bool RunsPipelineOnly => Messages.Count == 0;

    public static TimerTemplatePayload FromEntity(DomainTimer row) =>
        new()
        {
            Name = row.Name,
            Messages = [.. row.Messages],
            IntervalMinutes = row.IntervalMinutes,
            MinChatActivity = row.MinChatActivity,
            FireOnce = row.FireOnce,
            IsEnabled = row.IsEnabled,
        };

    public string ComputeHash() => PlatformTemplateJson.Hash(this);
}
