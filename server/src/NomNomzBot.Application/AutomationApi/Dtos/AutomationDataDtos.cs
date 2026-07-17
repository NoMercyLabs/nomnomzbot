// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace NomNomzBot.Application.AutomationApi.Dtos;

/// <summary>
/// The authenticated identity behind a data-plane call — resolved from a presented token secret.
/// Everything downstream trusts these fields, never the raw secret.
/// </summary>
public sealed record AutomationPrincipal(
    Guid BroadcasterId,
    Guid TokenId,
    string TokenName,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<Guid>? AllowedPipelineIds
);

/// <summary>Data-plane pipeline invocation; the pipeline may be named by id or by (unique) name.</summary>
public sealed record AutomationInvokeRequest
{
    public Guid? PipelineId { get; init; }

    [MaxLength(200)]
    public string? PipelineName { get; init; }

    public IReadOnlyList<string>? Args { get; init; }

    public IDictionary<string, string>? Variables { get; init; }
}

/// <summary>
/// The invoke outcome. Execution is fire-and-forget (automation-api.md D5): <see cref="Accepted"/>
/// means enqueued, and <see cref="ExecutionId"/> is the correlation id minted at enqueue time (also
/// visible to the pipeline as <c>{{automation.correlation_id}}</c>) — not a promise of completion.
/// </summary>
public sealed record AutomationInvokeResult(Guid PipelineId, Guid ExecutionId, bool Accepted);

/// <summary>Chat send: plain message, reply (with <see cref="ReplyToMessageId"/>), or whisper.</summary>
public sealed record AutomationChatRequest
{
    [Required]
    [MaxLength(500)]
    public string Text { get; init; } = null!;

    [MaxLength(255)]
    public string? ReplyToMessageId { get; init; }

    [MaxLength(50)]
    public string? WhisperToTwitchUserId { get; init; }
}

/// <summary>An invocable pipeline as the data plane lists it.</summary>
public sealed record AutomationPipelineRef(Guid Id, string Name);

/// <summary>A chat command as the data plane lists it.</summary>
public sealed record AutomationCommandRef(string Name, IReadOnlyList<string> Aliases);

/// <summary>Broadcaster + instance summary for <c>GET /automation/v1/info</c>.</summary>
public sealed record AutomationInfo(
    Guid ChannelId,
    string ChannelName,
    string Provider,
    string ApiVersion
);

/// <summary>One subscribable public event type (automation-api.md §3, for the events catalog).</summary>
public sealed record AutomationEventCatalogItem(string PublicName, string Description);
