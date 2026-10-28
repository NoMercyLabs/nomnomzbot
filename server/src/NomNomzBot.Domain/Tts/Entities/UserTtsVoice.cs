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
