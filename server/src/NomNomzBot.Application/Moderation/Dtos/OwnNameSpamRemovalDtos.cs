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

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// One moderator's own switch for one channel: may spam defence delete, time out and ban in their name there.
/// </summary>
/// <param name="Enabled">The moderator turned it on.</param>
/// <param name="Applies">
/// True when the switch decides anything: the channel did not onboard and the caller holds a management role on
/// it. An onboarded channel acts on its owner's access, so the switch has no effect there.
/// </param>
public sealed record OwnNameSpamRemovalDto(bool Enabled, bool Applies);

/// <summary>The moderator's choice.</summary>
public sealed record OwnNameSpamRemovalRequest(bool Enabled);
