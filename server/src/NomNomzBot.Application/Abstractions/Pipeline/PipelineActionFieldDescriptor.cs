// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// The shape a pipeline action's configuration field takes in the builder — drives which input control the
/// step form renders instead of a free-text box (S045). Resource-picker kinds map to a dashboard lookup
/// (e.g. <see cref="Widget"/> queries the channel's widgets); <see cref="ResourceId"/> is the fallback for a
/// domain entity reference that has no dedicated picker kind yet.
/// </summary>
public enum PipelineActionFieldKind
{
    Text,
    Number,
    Boolean,
    Enum,
    DiscordChannel,
    DiscordRole,
    TwitchUser,
    Reward,
    Widget,
    Voice,
    SoundClip,
    Asset,
    ResourceId,
}

/// <summary>
/// One configuration field an <see cref="ICommandAction"/> declares — the builder's step form renders one
/// control per field, keyed by <see cref="Kind"/>, instead of a free-text box (S045).
/// </summary>
/// <param name="Name">The <see cref="ActionDefinition.Parameters"/> key this field reads/writes.</param>
/// <param name="Kind">The input control kind the step form should render.</param>
/// <param name="Required">Whether the action fails validation without this field set.</param>
/// <param name="Repeatable">Whether the operator may add multiple values for this field (a segmented/list input).</param>
/// <param name="Options">
/// For <see cref="PipelineActionFieldKind.Enum"/> fields, the closed set of allowed values. Null for every
/// other kind.
/// </param>
public sealed record PipelineActionFieldDescriptor(
    string Name,
    PipelineActionFieldKind Kind,
    bool Required = false,
    bool Repeatable = false,
    IReadOnlyList<string>? Options = null
);

/// <summary>Converts a <see cref="PipelineActionFieldKind"/> to its snake_case wire name for the catalogue DTO.</summary>
public static class PipelineActionFieldKindExtensions
{
    public static string ToWireName(this PipelineActionFieldKind kind) =>
        kind switch
        {
            PipelineActionFieldKind.Text => "text",
            PipelineActionFieldKind.Number => "number",
            PipelineActionFieldKind.Boolean => "boolean",
            PipelineActionFieldKind.Enum => "enum",
            PipelineActionFieldKind.DiscordChannel => "discord_channel",
            PipelineActionFieldKind.DiscordRole => "discord_role",
            PipelineActionFieldKind.TwitchUser => "twitch_user",
            PipelineActionFieldKind.Reward => "reward",
            PipelineActionFieldKind.Widget => "widget",
            PipelineActionFieldKind.Voice => "voice",
            PipelineActionFieldKind.SoundClip => "sound_clip",
            PipelineActionFieldKind.Asset => "asset",
            PipelineActionFieldKind.ResourceId => "resource_id",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
        };
}
