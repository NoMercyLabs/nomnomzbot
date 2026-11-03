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

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The sample event a test fire hands to an event response: one value for every variable the event type's
/// preset declares. The acting-user variables are the caller's own; every other value comes from
/// <see cref="TemplateHelperSamples"/>, so nothing about a viewer, a count or a stream is invented here.
/// </summary>
public static class EventResponseTestSamples
{
    /// <summary>Sample tables key a whole family of variables under one pattern key.</summary>
    private static readonly string[][] FamilyPatterns =
    [
        ["obs.event.", "obs.event.<field>"],
        ["vts.event.", "vts.event.<field>"],
    ];

    /// <summary>The sample variables for <paramref name="eventType"/>, or null when it is not in the response table.</summary>
    public static Dictionary<string, string>? For(string eventType, EventResponseTestActor caller)
    {
        EventResponsePresetDto? preset = EventResponsePresetCatalog.Presets.FirstOrDefault(p =>
            p.EventType == eventType
        );
        if (preset is null)
            return null;

        Dictionary<string, string> sample = new(StringComparer.OrdinalIgnoreCase);
        foreach (string variable in preset.Variables)
            sample[variable] = CallerValue(variable, caller) ?? TableValue(variable);
        return sample;
    }

    private static string? CallerValue(string variable, EventResponseTestActor caller) =>
        variable switch
        {
            "user" or "viewer.name" => caller.DisplayName,
            "user.name" => caller.Login,
            "user.id" => caller.UserId,
            _ => null,
        };

    private static string TableValue(string variable)
    {
        string direct = TemplateHelperSamples.For(variable);
        if (direct.Length > 0)
            return direct;

        foreach (string[] family in FamilyPatterns)
            if (variable.StartsWith(family[0], StringComparison.OrdinalIgnoreCase))
                return TemplateHelperSamples.For(family[1]);
        return string.Empty;
    }
}
