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
using NomNomzBot.Application.Commands.Dtos;

namespace NomNomzBot.Application.Admin.Dtos;

/// <summary>
/// The answer to an admin's <c>diagnostics</c> read of one channel: one property per requested section. A section
/// that was not requested is omitted. A new section adds a property here and a class implementing
/// <c>IChannelDiagnosticSection</c>; never an endpoint.
/// </summary>
public sealed record ChannelDiagnosticsDto
{
    /// <summary>The channel these sections describe.</summary>
    public required Guid BroadcasterId { get; init; }

    /// <summary>The channel at a glance: name, plan, live state and how its runs went in the last 24 hours.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelDiagnosticSummaryDto? Summary { get; set; }

    /// <summary>One page of the channel's pipeline runs, newest first.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelDiagnosticRunsDto? Runs { get; set; }

    /// <summary>The channel's last 50 TTS clips and how long synthesis took over the last 24 hours.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelDiagnosticTtsDto? Tts { get; set; }
}

/// <summary>A channel's TTS clips, newest first, and the synthesis figures for the last 24 hours.</summary>
public sealed record ChannelDiagnosticTtsDto(
    IReadOnlyList<ChannelDiagnosticTtsClipDto> Clips,
    ChannelDiagnosticTtsWindowDto Last24Hours
);

/// <summary>One TTS clip. <paramref name="SynthesisMs"/> is null for a clip written before the timing was kept;
/// for a failed clip it is the time spent until the failure.</summary>
public sealed record ChannelDiagnosticTtsClipDto(
    DateTime OccurredAt,
    string Provider,
    string VoiceId,
    int CharacterCount,
    int? SynthesisMs,
    bool Failed
);

/// <summary>Synthesis over the last 24 hours. The average and the 95th percentile (nearest rank) cover every clip
/// with a measured time, failed ones included, and are null when no clip has one.</summary>
public sealed record ChannelDiagnosticTtsWindowDto(
    int Clips,
    int FailedClips,
    int? AverageSynthesisMs,
    int? P95SynthesisMs
);

/// <summary>A channel at a glance. The run counts cover the last 24 hours; the last failed run is the newest
/// failure of all time, so a quiet day still shows the last thing that broke.</summary>
public sealed record ChannelDiagnosticSummaryDto(
    string ChannelName,
    string TierKey,
    string TierDisplayName,
    bool IsLive,
    int RunsLast24Hours,
    int FailedRunsLast24Hours,
    long? LastFailedRunId,
    DateTime? LastFailedRunAt,
    string? LastFailedRunError
);

/// <summary>One page of a channel's runs, in the same row shape as the channel's own run history.</summary>
public sealed record ChannelDiagnosticRunsDto(
    IReadOnlyList<PipelineExecutionSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore
);
