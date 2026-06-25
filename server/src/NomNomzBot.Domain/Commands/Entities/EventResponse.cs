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
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// Configures what the bot does when a specific channel event fires
/// (e.g. a follow → send a chat message; a sub → trigger a pipeline).
/// Schema: I.2 (commands-pipelines.md §1).
/// </summary>
public class EventResponse : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>The event type this response applies to (e.g. "channel.follow", "channel.subscribe").</summary>
    [MaxLength(100)]
    public string EventType { get; set; } = null!;

    /// <summary>How the bot responds: chat_message | overlay | pipeline | none.</summary>
    [MaxLength(50)]
    public string ResponseType { get; set; } = "chat_message";

    /// <summary>The chat message template to send (when <see cref="ResponseType"/> is chat_message).</summary>
    [MaxLength(2000)]
    public string? Message { get; set; }

    /// <summary>FK to a named pipeline executed when <see cref="ResponseType"/> is pipeline.</summary>
    public Guid? PipelineId { get; set; }

    [ForeignKey(nameof(PipelineId))]
    public virtual Pipeline? Pipeline { get; set; }

    /// <summary>Additional key-value metadata (e.g. overlay widget id).</summary>
    public Dictionary<string, string> MetadataJson { get; set; } = new();

    /// <summary>EF Core schema version.</summary>
    public int ConfigSchemaVersion { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
