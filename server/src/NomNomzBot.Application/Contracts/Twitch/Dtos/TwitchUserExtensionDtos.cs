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

// Helix "Users" extension wire models (GET /users/extensions/list, GET/PUT /users/extensions). These
// records deserialize straight from Twitch's snake_case JSON via the transport's naming policy — no
// per-property annotations; dictionary keys (the slot numbers "1", "2", …) pass through untouched because
// the policy only renames properties. Twitch ids stay strings; the owning tenant is always passed in as a
// Guid method argument, never here.

/// <summary>
/// One extension the broadcaster has installed (Get User Extensions), active or not.
/// <see cref="CanActivate"/> is false while the extension is not yet configured; <see cref="Type"/> lists
/// the slot types it can be activated in (<c>component</c> / <c>mobile</c> / <c>overlay</c> / <c>panel</c>).
/// </summary>
public sealed record TwitchInstalledExtension(
    string Id,
    string Version,
    string Name,
    bool CanActivate,
    IReadOnlyList<string> Type
);

/// <summary>
/// The broadcaster's activated extensions (Get/Update User Active Extensions): one slot map per slot type,
/// keyed by the slot number Twitch assigns sequentially from <c>"1"</c>. The wire <c>data</c> is a single
/// nested object, so the transport's envelope wraps exactly one of these.
/// </summary>
public sealed record TwitchActiveExtensions(
    IReadOnlyDictionary<string, TwitchActiveExtensionSlot> Panel,
    IReadOnlyDictionary<string, TwitchActiveExtensionSlot> Overlay,
    IReadOnlyDictionary<string, TwitchActiveExtensionSlot> Component
);

/// <summary>
/// One activation slot. When <see cref="Active"/> is false Twitch omits every other field; <see cref="X"/>
/// and <see cref="Y"/> (the placement coordinate) only exist on component slots.
/// </summary>
public sealed record TwitchActiveExtensionSlot(
    bool Active,
    string? Id = null,
    string? Version = null,
    string? Name = null,
    int? X = null,
    int? Y = null
);

/// <summary>
/// Update User Extensions request body. Twitch wraps the slot maps in a top-level <c>data</c> object, so
/// this record models that wrapper verbatim and is sent as-is.
/// </summary>
public sealed record UpdateUserExtensionsRequest(UpdateUserExtensionsData Data);

/// <summary>
/// The slot maps being written (Update User Extensions), keyed by slot number from <c>"1"</c>. Only the
/// maps the caller sets are sent — the transport omits nulls.
/// </summary>
public sealed record UpdateUserExtensionsData(
    IReadOnlyDictionary<string, TwitchExtensionSlotUpdate>? Panel = null,
    IReadOnlyDictionary<string, TwitchExtensionSlotUpdate>? Overlay = null,
    IReadOnlyDictionary<string, TwitchExtensionSlotUpdate>? Component = null
);

/// <summary>
/// One slot write: activate (<see cref="Id"/> + <see cref="Version"/> required by Twitch, plus
/// <see cref="X"/>/<see cref="Y"/> for component slots) or deactivate (<c>Active</c> false alone).
/// </summary>
public sealed record TwitchExtensionSlotUpdate(
    bool Active,
    string? Id = null,
    string? Version = null,
    int? X = null,
    int? Y = null
);
