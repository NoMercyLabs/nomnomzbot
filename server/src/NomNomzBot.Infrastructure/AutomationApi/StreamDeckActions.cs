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

using System.Text.Json;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Infrastructure.AutomationApi;

/// <summary>
/// The action families a Stream Deck key may run by name: "NomNomzBot: Music" (<c>music_*</c>) and
/// "NomNomzBot: OBS" (<c>obs_*</c>). A key names its action; the automation API runs it as an action
/// target (commands-pipelines.md section 12) instead of a wrapper pipeline. A new device family adds
/// its prefix here.
/// </summary>
internal static class StreamDeckActions
{
    internal const string MusicPrefix = "music_";
    private static readonly string[] Prefixes = [MusicPrefix, "obs_"];

    internal static bool IsStreamDeckAction(string actionType) =>
        Prefixes.Any(prefix => actionType.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// The target for one key press. Each field of the action gets the literal <c>{field}</c> placeholder,
    /// which the action resolves against the key's own settings (sent as the invoke variables).
    /// </summary>
    internal static BindingTarget TargetFor(ICommandAction action)
    {
        Dictionary<string, JsonElement>? parameters = null;
        if (action.Fields is { Count: > 0 })
        {
            parameters = new(StringComparer.Ordinal);
            foreach (PipelineActionFieldDescriptor field in action.Fields)
                parameters[field.Name] = JsonSerializer.SerializeToElement($"{{{field.Name}}}");
        }

        return new(BindingTargetKind.Action, ActionType: action.ActionType, Params: parameters);
    }
}
