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

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.PlatformContent;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// Configures what the bot does when a specific channel event fires
/// (e.g. a follow → send a chat message; a sub → trigger a pipeline).
/// Schema: I.2 (commands-pipelines.md §1).
/// </summary>
/// <remarks>
/// NOT a <c>[CountedResource]</c> (S-EVENTRESPONSE-NO-CREATE): rows are a fixed, seeded catalogue — one
/// per known event type (<see cref="Content.Commands.EventResponsePresetCatalog"/>) — never user-created,
/// so a per-channel limit on this resource can never be reached and would be decorative. See
/// commands-pipelines.md §3.8.
/// </remarks>
public class EventResponse
    : SoftDeletableEntity,
        ITenantScoped,
        IPlatformSourced,
        IBindingTargetHolder
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
    [TemplatedUserContent]
    public string? Message { get; set; }

    /// <summary>FK to a named pipeline executed when <see cref="ResponseType"/> is pipeline.</summary>
    public Guid? PipelineId { get; set; }

    /// <inheritdoc cref="IBindingTargetHolder.TargetKind"/>
    public BindingTargetKind TargetKind { get; set; } = BindingTargetKind.Pipeline;

    /// <inheritdoc cref="IBindingTargetHolder.TargetCommandId"/>
    public Guid? TargetCommandId { get; set; }

    /// <inheritdoc cref="IBindingTargetHolder.TargetActionType"/>
    [MaxLength(100)]
    public string? TargetActionType { get; set; }

    /// <inheritdoc cref="IBindingTargetHolder.TargetActionParamsJson"/>
    public string? TargetActionParamsJson { get; set; }

    Guid? IBindingTargetHolder.LegacyPipelineId
    {
        get => PipelineId;
        set => PipelineId = value;
    }

    [ForeignKey(nameof(PipelineId))]
    public virtual Pipeline? Pipeline { get; set; }

    /// <summary>Additional key-value metadata (e.g. overlay widget id).</summary>
    public Dictionary<string, string> MetadataJson { get; set; } = new();

    /// <summary>EF Core schema version.</summary>
    public int ConfigSchemaVersion { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// When <see cref="ResponseType"/> is chat_message, the resolved message is also spoken through the
    /// channel's TTS (same text, same TTS gates as a pipeline's play_tts step). Opt-in: false by default.
    /// </summary>
    public bool SpeakWithTts { get; set; }

    /// <summary>
    /// Where a chat_message response goes (chat, chat overlay, both, nowhere) and whether the overlay line is
    /// HTML. Stored in the <c>ReplyDeliveryJson</c> column; null = the platform chat as plain text, so every row
    /// written before this setting existed behaves exactly as it did.
    /// </summary>
    public ReplyDelivery? ReplyDelivery { get; set; }

    /// <summary>
    /// True while the channel has never saved its own response for this event: the runtime then uses the
    /// <see cref="PlatformEventResponseDefault"/> for the event type instead of this row's own values. Saving
    /// a response clears it; resetting the response sets it again.
    /// </summary>
    public bool FollowsPlatformDefault { get; set; }

    public Guid? PlatformSourceDefinitionId { get; set; }

    public int? PlatformSourceVersion { get; set; }

    [MaxLength(64)]
    public string? PlatformSourceHash { get; set; }

    public DateTime? PlatformSourceSyncedAt { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
