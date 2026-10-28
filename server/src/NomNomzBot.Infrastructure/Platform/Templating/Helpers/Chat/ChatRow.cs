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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>What a render of one chat, tts or link token needs.</summary>
internal readonly record struct ChatCall(
    ChatData? Data,
    IReadOnlyList<string> Args,
    TemplateRenderScope Scope,
    CancellationToken Ct
)
{
    /// <summary>The chat platform that delivers the line: the triggering user's platform, Twitch when no seed names one.</summary>
    public string Provider =>
        Scope.Variables.TryGetValue("user.provider", out string? provider) && provider.Length > 0
            ? provider
            : "twitch";

    /// <summary>A seed variable, or null when the caller did not seed it.</summary>
    public string? Seed(string key) =>
        Scope.Variables.TryGetValue(key, out string? value) ? value : null;
}

/// <summary>One <c>chat.*</c>, <c>tts.*</c> or <c>link.*</c> helper: its key, sample, argument count and renderer.</summary>
internal sealed record ChatRow(
    string Key,
    string Sample,
    ChatSources Sources,
    int MinArgs,
    int MaxArgs,
    Func<ChatCall, Task<string?>> Render
)
{
    public TemplateHelperFreshness Freshness { get; init; } = TemplateHelperFreshness.Pure;

    /// <summary>Whose data the row reads (the viewer who typed the command, or the channel).</summary>
    public TemplateHelperScope Scope { get; init; } = TemplateHelperScope.Global;

    /// <summary>A row computed from its arguments alone; it needs no channel.</summary>
    public static ChatRow Pure(
        string key,
        string sample,
        int minArgs,
        int maxArgs,
        Func<ChatCall, string?> render
    ) =>
        new(key, sample, ChatSources.None, minArgs, maxArgs, call => Task.FromResult(render(call)));

    /// <summary>A pure row whose only argument is the text.</summary>
    public static ChatRow OfText(
        string key,
        string sample,
        Func<string, ChatCall, string?> render
    ) => Pure(key, sample, 1, 1, call => render(call.Args[0], call));

    /// <summary>A row without arguments that reads the trigger's seed variables.</summary>
    public static ChatRow Seeded(string key, string sample, Func<ChatCall, string?> render) =>
        Pure(key, sample, 0, 0, render) with
        {
            Freshness = TemplateHelperFreshness.Seed,
            Scope = TemplateHelperScope.Viewer,
        };

    /// <summary>A row without arguments that reads the channel through <paramref name="sources"/>.</summary>
    public static ChatRow Read(
        string key,
        string sample,
        ChatSources sources,
        TemplateHelperFreshness freshness,
        Func<ChatData, ChatCall, Task<string?>> render
    ) =>
        new(key, sample, sources, 0, 0, WithData(render))
        {
            Freshness = freshness,
            Scope = TemplateHelperScope.Channel,
        };

    /// <summary>A row whose only argument is the text and that reads the channel through <paramref name="sources"/>.</summary>
    public static ChatRow ReadText(
        string key,
        string sample,
        ChatSources sources,
        TemplateHelperFreshness freshness,
        Func<ChatData, string, ChatCall, Task<string?>> render
    ) =>
        new(key, sample, sources, 1, 1, WithData((data, call) => render(data, call.Args[0], call)))
        {
            Freshness = freshness,
            Scope = TemplateHelperScope.Channel,
        };

    public string Namespace => HelperNode.NamespaceOf(Key);

    public TemplateHelperEntry ToEntry() =>
        new(Key, TemplateHelperContexts.Standard, new LocalizedText("template.helper." + Key))
        {
            Sample = Sample,
            Source = Sources == ChatSources.None ? null : Sources.ToString().ToLowerInvariant(),
            Freshness = Freshness,
            Scope = Scope,
        };

    private static Func<ChatCall, Task<string?>> WithData(
        Func<ChatData, ChatCall, Task<string?>> render
    ) => call => call.Data is { } data ? render(data, call) : Task.FromResult<string?>(null);
}
