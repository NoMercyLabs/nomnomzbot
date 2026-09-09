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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Tts.Entities;

public class UserTtsVoice : BaseEntity, ITenantScoped
{
    // Surrogate UUIDv7 PK (schema P.3: "Id guid PK (was int -> surrogate)") — was a bare int, drifted from
    // the locked schema and from every other per-user entity's own PK convention (ViewerDatum,
    // ChannelCommunityStanding, PermitGrant, …). Fixed via a real migration; nothing else referenced the old
    // int Id (verified — no FK, no serialized DTO exposed it), so this is a pure type correction.
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }

    [MaxLength(50)]
    public string UserId { get; set; } = null!;

    [MaxLength(255)]
    public string VoiceId { get; set; } = null!;
}
