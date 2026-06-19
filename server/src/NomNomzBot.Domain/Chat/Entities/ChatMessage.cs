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
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Chat.Entities;

public class ChatMessage : SoftDeletableEntity
{
    [MaxLength(255)]
    public string Id { get; set; } = null!;

    [MaxLength(50)]
    public string BroadcasterId { get; set; } = null!;

    [MaxLength(50)]
    public string UserId { get; set; } = null!;

    [MaxLength(255)]
    public string Username { get; set; } = null!;

    [MaxLength(255)]
    public string DisplayName { get; set; } = null!;

    [MaxLength(20)]
    public string UserType { get; set; } = null!;

    [MaxLength(7)]
    public string? ColorHex { get; set; }

    public string Message { get; set; } = null!;

    public List<ChatMessageFragment> Fragments { get; set; } = [];
    public List<ChatBadge> Badges { get; set; } = [];

    [MaxLength(50)]
    public string MessageType { get; set; } = "text";

    public bool IsCommand { get; set; }
    public bool IsCheer { get; set; }
    public int? BitsAmount { get; set; }
    public bool IsHighlighted { get; set; }

    [MaxLength(255)]
    public string? ReplyToMessageId { get; set; }

    [MaxLength(50)]
    public string? StreamId { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;

    [ForeignKey(nameof(StreamId))]
    public virtual global::NomNomzBot.Domain.Stream.Entities.Stream? Stream { get; set; }
}
