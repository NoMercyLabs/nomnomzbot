// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Api.Identifiers;

/// <summary>
/// A binding target names its command or pipeline in a string id, which <see cref="UlidGuidJsonConverter"/> never
/// sees. This gives that id the owned-id form every other id has: a ULID on the way out, and a ULID or a Guid on
/// the way in, read as the Guid string the binder parses. The other fields pass through unchanged.
/// </summary>
public sealed class BindingTargetJsonConverter : JsonConverter<BindingTarget>
{
    public override BindingTarget? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        Wire? wire = JsonSerializer.Deserialize<Wire>(ref reader, options);
        return wire is null
            ? null
            : new(wire.Kind, OwnedIdCodec.FromWire(wire.Id), wire.ActionType, wire.Params);
    }

    public override void Write(
        Utf8JsonWriter writer,
        BindingTarget value,
        JsonSerializerOptions options
    ) =>
        JsonSerializer.Serialize(
            writer,
            new Wire(value.Kind, OwnedIdCodec.ToWire(value.Id), value.ActionType, value.Params),
            options
        );

    /// <summary>The same shape as <see cref="BindingTarget"/>, so the options name and skip its fields alike.</summary>
    private sealed record Wire(
        BindingTargetKind Kind,
        string? Id,
        string? ActionType,
        IReadOnlyDictionary<string, JsonElement>? Params
    );
}
