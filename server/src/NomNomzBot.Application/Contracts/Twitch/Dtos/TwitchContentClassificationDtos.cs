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

// Helix "Content Classification Labels" wire model (GET /content_classification_labels). This record
// deserializes straight from Twitch's snake_case JSON via the transport's naming policy — no per-property
// annotations. The endpoint is an App-token, free-text read: there is no owning tenant here, so the label id
// stays a string (a stable Twitch label identifier such as "DrugsIntoxication"), localized by the optional
// locale query.

/// <summary>
/// One content classification label (CCL) — the categories a broadcaster may flag on their channel/stream.
/// <c>Id</c> is the stable label identifier; <c>Name</c> and <c>Description</c> are localized to the requested
/// locale (default <c>en-US</c>).
/// </summary>
public sealed record TwitchContentClassificationLabel(string Id, string Name, string Description);
