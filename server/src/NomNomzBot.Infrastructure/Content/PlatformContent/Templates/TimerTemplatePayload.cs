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
