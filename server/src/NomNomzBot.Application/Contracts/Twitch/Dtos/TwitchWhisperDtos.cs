// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Whispers" category wire model (POST /whispers). Send Whisper returns 204 with no body, so
// there is no response record — only the request body. The sender and recipient are not part of this
// body: the sender is the owning tenant (passed as a Guid method argument and resolved to its Twitch id
// for the from_user_id query param), and the recipient is a raw Twitch id passed as the to_user_id query
// param. This record serializes to Twitch's snake_case JSON via the transport's naming policy.

/// <summary>Send Whisper request body — the whisper text (<c>message</c>).</summary>
public sealed record SendWhisperRequest(string Message);
