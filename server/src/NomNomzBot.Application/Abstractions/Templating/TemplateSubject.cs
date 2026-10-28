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

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// The one place that decides what the identity keys of a person hold, so a handler never writes them by hand
/// and a key never carries the wrong kind of value. For the prefix <c>user</c> (the viewer an event is about)
/// and <c>target</c> (the person a command or action points at):
/// <list type="bullet">
///   <item><c>user</c> / <c>target.displayname</c> — the name as the platform shows it.</item>
///   <item><c>user.name</c> / <c>target</c> / <c>target.name</c> — the login: lowercase, no spaces.</item>
///   <item><c>user.id</c> / <c>target.id</c> — the platform id.</item>
/// </list>
/// A kind that is not known is left out; it is never guessed from another kind.
/// </summary>
public static class TemplateSubject
{
    /// <summary>Adds the <c>user</c> identity keys that are not already in <paramref name="variables"/>.</summary>
    public static void SeedUser(
        IDictionary<string, string> variables,
        string? id,
        string? login,
        string? displayName
    )
    {
        string? shown = string.IsNullOrWhiteSpace(displayName) ? login : displayName;
        if (!string.IsNullOrWhiteSpace(shown))
            variables.TryAdd("user", shown);
        if (!string.IsNullOrWhiteSpace(login))
            variables.TryAdd("user.name", login);
        if (!string.IsNullOrWhiteSpace(id))
            variables.TryAdd("user.id", id);
    }

    /// <summary>
    /// Adds the <c>target</c> identity keys that are not already in <paramref name="variables"/>, copied from the
    /// <c>user</c> keys: an event has no command argument, so the person it is about is the target.
    /// </summary>
    public static void SeedTargetFromUser(IDictionary<string, string> variables)
    {
        bool hasLogin = variables.TryGetValue("user.name", out string? login);
        if (hasLogin)
        {
            variables.TryAdd("target", login!);
            variables.TryAdd("target.name", login!);
        }

        if (variables.TryGetValue("user", out string? shown))
        {
            variables.TryAdd("target.displayname", shown);
            if (!hasLogin)
                variables.TryAdd("target", shown);
        }

        if (variables.TryGetValue("user.id", out string? id))
            variables.TryAdd("target.id", id);
        if (variables.TryGetValue("user.link", out string? link))
            variables.TryAdd("target.link", link);
    }

    /// <summary>
    /// The keys of an actor that is not the viewer — a gifter, a moderator — under <paramref name="prefix"/>:
    /// the key itself holds the shown name, <c>.id</c> the platform id. The id never stands in for the name.
    /// </summary>
    public static void SeedActor(
        IDictionary<string, string> variables,
        string prefix,
        string? id,
        string? displayName
    )
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            variables.TryAdd(prefix, displayName);
        if (!string.IsNullOrWhiteSpace(id))
            variables.TryAdd($"{prefix}.id", id);
    }
}
