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

    /// <summary>One row per platform and integration link the channel has, with its health. Never a token value.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ChannelDiagnosticConnectionDto>? Connections { get; set; }
}

/// <summary>
/// The health of one connection a channel has: a platform link, an integration, a Discord server, OBS, VTube
/// Studio, an automation token, a supporter source, a webhook or a legacy service row. It holds states and times
/// only, never a token, secret or URL token. <see cref="Kind"/> is one of <c>ChannelConnectionKinds</c>.
/// </summary>
public sealed record ChannelDiagnosticConnectionDto
{
    /// <summary>The family of the row (platform, integration, discordGuild, obs, vts, automationToken, supporter, webhook, service).</summary>
    public required string Kind { get; init; }

    /// <summary>The provider inside the family (twitch, spotify, discord, ...).</summary>
    public required string Provider { get; init; }

    /// <summary>The row's own id, for keying a list.</summary>
    public required string Id { get; init; }

    /// <summary>The account, server, host or label the connection points at; null when the row has none.</summary>
    public string? AccountName { get; init; }

    /// <summary>The provider's id for that account; null when the row has none.</summary>
    public string? AccountId { get; init; }

    /// <summary>Whether the connection works now.</summary>
    public required bool Connected { get; init; }

    /// <summary>The stored status as the code base writes it (connected, needs_reauth, decrypt_failed, enabled, error, ...).</summary>
    public string? Status { get; init; }

    /// <summary>The streamer must sign in to the provider again.</summary>
    public bool NeedsReauth { get; init; }

    /// <summary>The stored token cannot be decrypted, so only a fresh grant helps.</summary>
    public bool DecryptFailed { get; init; }

    /// <summary>Scopes the code needs that the connection does not hold; only the Twitch connection has a registry of them.</summary>
    public IReadOnlyList<string> MissingScopes { get; init; } = [];

    /// <summary>Scopes the provider granted, where the row stores them.</summary>
    public IReadOnlyList<string> GrantedScopes { get; init; } = [];

    /// <summary>When the access token or the automation token expires.</summary>
    public DateTime? TokenExpiresAt { get; init; }

    /// <summary>The last error text, where the row stores one.</summary>
    public string? LastError { get; init; }

    /// <summary>When the last error happened, where the row stores it.</summary>
    public DateTime? LastErrorAt { get; init; }

    /// <summary>Failures in a row since the last success, where the row counts them.</summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>The last sign of life: a connect, a token refresh, an event, a delivery or a use of the token.</summary>
    public DateTime? LastActivityAt { get; init; }
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
