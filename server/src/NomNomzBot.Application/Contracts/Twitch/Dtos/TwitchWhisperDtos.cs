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

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Whispers" category wire model (POST /whispers). Send Whisper returns 204 with no body, so
// there is no response record — only the request body. The sender and recipient are not part of this
// body: the sender is the owning tenant (passed as a Guid method argument and resolved to its Twitch id
// for the from_user_id query param), and the recipient is a raw Twitch id passed as the to_user_id query
// param. This record serializes to Twitch's snake_case JSON via the transport's naming policy.

/// <summary>Send Whisper request body — the whisper text (<c>message</c>).</summary>
public sealed record SendWhisperRequest(string Message);
