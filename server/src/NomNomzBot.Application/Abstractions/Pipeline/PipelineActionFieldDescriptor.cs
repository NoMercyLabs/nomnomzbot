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

using NomNomzBot.Application.Abstractions.Localization;

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
    PickList,
    Asset,
    ResourceId,

    /// <summary>One of the channel's custom-code scripts, picked by name; the saved value is the script id.</summary>
    Script,

    /// <summary>One playlist of the channel's connected music provider, picked by name; the saved value is the provider's playlist id.</summary>
    MusicPlaylist,

    /// <summary>One of the channel's channel-point rewards, picked by title; the saved value is the Twitch reward id a widget event carries.</summary>
    TwitchReward,

    /// <summary>One audio input of the connected OBS (an input that has a mute state), picked by name; the saved value is the input name OBS addresses it by.</summary>
    ObsAudioInput,

    /// <summary>One scene of the connected OBS, picked by name; the saved value is the scene name OBS addresses it by.</summary>
    ObsScene,

    /// <summary>One source placed in a scene of the connected OBS, picked by name. A sibling <c>scene</c> field narrows the list to that scene; the saved value is the source name.</summary>
    ObsSceneItem,

    /// <summary>One filter on a source of the connected OBS, picked by name. A sibling <c>source</c> field narrows the list to that source; the saved value is the filter name.</summary>
    ObsFilter,

    /// <summary>One scene transition of the connected OBS, picked by name; the saved value is the transition name.</summary>
    ObsTransition,

    /// <summary>Any input of the connected OBS (audio, media, browser, image), picked by name; the saved value is the input name.</summary>
    ObsInput,

    /// <summary>One hotkey OBS knows about, picked by name; the saved value is the hotkey name OBS fires it by.</summary>
    ObsHotkey,

    /// <summary>One model of the connected VTube Studio, picked by name; the saved value is the VTS model id.</summary>
    VtsModel,

    /// <summary>One hotkey of the model loaded in VTube Studio, picked by name; the saved value is the VTS hotkey id.</summary>
    VtsHotkey,

    /// <summary>One expression file of the model loaded in VTube Studio; the saved value is the expression file name.</summary>
    VtsExpression,

    /// <summary>One economy game of the channel; the saved value is the game type key.</summary>
    GameType,

    /// <summary>One live game the platform ships (for example a drop game); the saved value is the game key.</summary>
    LiveGameType,

    /// <summary>One pronoun from the pronoun catalogue, or the choice to clear the override; the saved value is the pronoun name or <c>clear</c>.</summary>
    Pronoun,

    /// <summary>One playback device of the channel's music provider; the saved value is the provider's device id.</summary>
    MusicDevice,

    /// <summary>One track found by searching the channel's music provider; the saved value is the track URI.</summary>
    MusicTrack,

    /// <summary>One of the channel's pipelines, saved by its id (the form <c>run_pipeline</c> runs).</summary>
    Pipeline,

    /// <summary>One of the channel's pipelines, saved by its name (the form <c>schedule_pipeline</c> looks up).</summary>
    PipelineName,

    /// <summary>One of the channel's commands, saved by its name.</summary>
    CommandName,

    /// <summary>One registered pipeline action type; the saved value is the action type key.</summary>
    ActionType,

    /// <summary>One savings jar the channel owns or has joined, saved by its id.</summary>
    SavingsJar,

    /// <summary>One of the channel's giveaways, saved by its id.</summary>
    Giveaway,

    /// <summary>One of the channel's quotes; the saved value is the quote number.</summary>
    Quote,

    /// <summary>One outbound webhook endpoint of the channel, saved by its id.</summary>
    WebhookEndpoint,

    /// <summary>
    /// A labelled name→value map (S-PIPE-TREE-d2b(a)) — the builder renders one labelled field per
    /// declared name instead of an unlabelled positional list. Used by <c>run_pipeline</c>'s
    /// <c>named_args</c> field to bind a sub-pipeline call's arguments BY NAME rather than by
    /// position.
    /// </summary>
    KeyValueMap,

    /// <summary>One member of the linked Discord server, searched and picked by name; the saved value is the Discord user id.</summary>
    DiscordUser,
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
/// <param name="Templated">
/// Whether this field's stored value is a template (<c>{{user.name}}</c>-style placeholders get resolved
/// against the run's variables) rather than literal text (S-PIPE-TREE-d2b(b)). Defaults to <c>false</c> —
/// safe-by-default, so a field only renders when an action deliberately opts in; a regex, a raw id, or a
/// routing key where <c>{{</c> is legitimate literal content stays untouched. Visible to the dashboard's step
/// form via the <c>GET pipelines/actions</c> catalogue, so an author can tell which fields accept templates.
/// When <see cref="ICommandAction.ResolvesOwnTemplates"/> is true the action resolves this field itself
/// instead of the engine's central pass — either way the field is templated exactly once.
/// </param>
/// <param name="Description">
/// Operator-facing help text for the step form's field (S-SCHEMA-I18N-c) — what the field DOES and what
/// changes when it's set, in plain language for a streamer. <see cref="LocalizedText"/> is a translation KEY
/// only (resolved against <c>strings.xml</c> in the dashboard, both <c>en</c> and <c>nl</c>).
/// <see cref="PipelineActionFieldCoverageTests"/> enforces this at test time for every field on every
/// registered <see cref="ICommandAction"/> found via the real DI scan — a field shipped without a
/// <see cref="Description"/> fails that guard loudly, by field name, instead of silently shipping unhelped. It
/// stays nullable on the record itself (rather than a required constructor parameter) only because a required
/// positional parameter would force every one of the ~110 existing field sites to be touched atomically in a
/// single commit; the DI-graph guard gives the same "cannot ship without it" property without that coupling.
/// </param>
/// <param name="DeclaresVariable">
/// Whether this field's value is the NAME of a variable the action writes for later steps
/// (<c>pick_from_list</c>'s <c>variable</c>, <c>set_variable</c>'s <c>name</c>). The save-time template guard
/// accepts <c>{name}</c> in every templated field of the same pipeline for each such name.
/// </param>
/// <param name="DeclaredVariableDefault">
/// The variable name the action writes when a <see cref="DeclaresVariable"/> field is left empty
/// (e.g. <c>pick</c>); null when the action writes nothing without it.
/// </param>
public sealed record PipelineActionFieldDescriptor(
    string Name,
    PipelineActionFieldKind Kind,
    bool Required = false,
    bool Repeatable = false,
    IReadOnlyList<string>? Options = null,
    bool Templated = false,
    LocalizedText? Description = null,
    bool DeclaresVariable = false,
    string? DeclaredVariableDefault = null
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
            PipelineActionFieldKind.DiscordUser => "discord_user",
            PipelineActionFieldKind.TwitchUser => "twitch_user",
            PipelineActionFieldKind.Reward => "reward",
            PipelineActionFieldKind.Widget => "widget",
            PipelineActionFieldKind.Voice => "voice",
            PipelineActionFieldKind.SoundClip => "sound_clip",
            PipelineActionFieldKind.PickList => "pick_list",
            PipelineActionFieldKind.Asset => "asset",
            PipelineActionFieldKind.ResourceId => "resource_id",
            PipelineActionFieldKind.Script => "script",
            PipelineActionFieldKind.MusicPlaylist => "music_playlist",
            PipelineActionFieldKind.KeyValueMap => "key_value_map",
            PipelineActionFieldKind.TwitchReward => "twitch_reward",
            PipelineActionFieldKind.ObsAudioInput => "obs_audio_input",
            PipelineActionFieldKind.ObsScene => "obs_scene",
            PipelineActionFieldKind.ObsSceneItem => "obs_scene_item",
            PipelineActionFieldKind.ObsFilter => "obs_filter",
            PipelineActionFieldKind.ObsTransition => "obs_transition",
            PipelineActionFieldKind.ObsInput => "obs_input",
            PipelineActionFieldKind.ObsHotkey => "obs_hotkey",
            PipelineActionFieldKind.VtsModel => "vts_model",
            PipelineActionFieldKind.VtsHotkey => "vts_hotkey",
            PipelineActionFieldKind.VtsExpression => "vts_expression",
            PipelineActionFieldKind.GameType => "game_type",
            PipelineActionFieldKind.LiveGameType => "live_game_type",
            PipelineActionFieldKind.Pronoun => "pronoun",
            PipelineActionFieldKind.MusicDevice => "music_device",
            PipelineActionFieldKind.MusicTrack => "music_track",
            PipelineActionFieldKind.Pipeline => "pipeline",
            PipelineActionFieldKind.PipelineName => "pipeline_name",
            PipelineActionFieldKind.CommandName => "command_name",
            PipelineActionFieldKind.ActionType => "action_type",
            PipelineActionFieldKind.SavingsJar => "savings_jar",
            PipelineActionFieldKind.Giveaway => "giveaway",
            PipelineActionFieldKind.Quote => "quote",
            PipelineActionFieldKind.WebhookEndpoint => "webhook_endpoint",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
        };

    /// <summary>Finds the kind whose wire name is <paramref name="wireName"/> (ignoring case).</summary>
    public static bool TryFromWireName(string? wireName, out PipelineActionFieldKind kind)
    {
        foreach (PipelineActionFieldKind candidate in Enum.GetValues<PipelineActionFieldKind>())
        {
            if (string.Equals(candidate.ToWireName(), wireName, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    /// <summary>True for a kind that renders a searchable lookup list; false for the plain controls and the id fallback.</summary>
    public static bool IsResourcePicker(this PipelineActionFieldKind kind) =>
        kind
            is not (
                PipelineActionFieldKind.Text
                or PipelineActionFieldKind.Number
                or PipelineActionFieldKind.Boolean
                or PipelineActionFieldKind.Enum
                or PipelineActionFieldKind.ResourceId
                or PipelineActionFieldKind.KeyValueMap
            );
}
