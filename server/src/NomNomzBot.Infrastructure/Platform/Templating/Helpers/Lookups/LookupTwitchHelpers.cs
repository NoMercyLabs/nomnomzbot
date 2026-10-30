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

using System.Globalization;
using System.Text.RegularExpressions;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// <c>lookup.twitch:&lt;login&gt;.&lt;field&gt;</c>: facts about any Twitch user, read through the bot's own
/// Helix client. The follower count and the follow age need a moderator scope on the channel they read, so they
/// answer for the channel the template runs in only; for any other login the token stays as written.
/// </summary>
internal sealed partial class LookupTwitchHelpers()
    : LookupHelperGroup(
        new LookupRow(
            "lookup.twitch",
            "Minecraft",
            "twitch",
            TemplateHelperFreshness.Live,
            1,
            1,
            RenderAsync
        )
    )
{
    private const string FollowagePrefix = "followage:";

    private static readonly Dictionary<string, Func<LookupCall, string, Task<string?>>> Fields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["game"] = (call, login) => ChannelText(call, login, c => c.GameName),
            ["lastGame"] = (call, login) => ChannelText(call, login, c => c.GameName),
            ["title"] = (call, login) => ChannelText(call, login, c => c.Title),
            ["display"] = (call, login) => UserText(call, login, u => u.DisplayName),
            ["avatar"] = (call, login) => UserText(call, login, u => u.ProfileImageUrl),
            ["createdAt"] = (call, login) =>
                UserText(
                    call,
                    login,
                    u =>
                        u.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                ),
            ["isLive"] = (call, login) => LiveText(call, login, s => s is null ? "false" : "true"),
            ["viewers"] = (call, login) =>
                LiveText(call, login, s => s?.ViewerCount.ToString("N0", call.Culture) ?? ""),
            ["uptime"] = (call, login) =>
                LiveText(
                    call,
                    login,
                    s => s is null ? "" : StreamText.Short(call.Scope.Now - s.StartedAt)
                ),
            ["followers"] = FollowersAsync,
            ["url"] = (_, login) =>
                Task.FromResult<string?>("https://twitch.tv/" + login.ToLowerInvariant()),
        };

    [GeneratedRegex("^[A-Za-z0-9_]{1,25}$")]
    private static partial Regex LoginPattern();

    private static async Task<string?> RenderAsync(LookupCall call)
    {
        string argument = ParsedArgument.Word(call.Args[0]).TrimStart('@');
        int dot = argument.IndexOf('.');
        if (dot < 0 || !LoginPattern().IsMatch(argument[..dot]))
            return null;

        string login = argument[..dot];
        string field = argument[(dot + 1)..];
        if (field.StartsWith(FollowagePrefix, StringComparison.OrdinalIgnoreCase))
            return await FollowageAsync(call, login, field[FollowagePrefix.Length..]);

        return Fields.TryGetValue(field, out Func<LookupCall, string, Task<string?>>? render)
            ? await render(call, login)
            : null;
    }

    private static async Task<string?> UserText(
        LookupCall call,
        string login,
        Func<TwitchUser, string> read
    ) =>
        await call.Data.UserAsync(login, call.Ct) is { } user
            ? read(user)
            : call.Fallback(LookupFallbacks.Twitch);

    private static async Task<string?> ChannelText(
        LookupCall call,
        string login,
        Func<TwitchChannelInformation, string> read
    ) =>
        await call.Data.ChannelAsync(login, call.Ct) is { } channel
            ? read(channel)
            : call.Fallback(LookupFallbacks.Twitch);

    private static async Task<string?> LiveText(
        LookupCall call,
        string login,
        Func<TwitchStream?, string> read
    ) =>
        await call.Data.StreamAsync(login, call.Ct) is { } state
            ? read(state.Stream)
            : call.Fallback(LookupFallbacks.Twitch);

    private static async Task<string?> FollowersAsync(LookupCall call, string login)
    {
        if (!call.IsOwnChannel(login))
            return null;

        return await call.Data.FollowerCountAsync(call.Ct) is { } count
            ? count.ToString("N0", call.Culture)
            : call.Fallback(LookupFallbacks.Twitch);
    }

    private static async Task<string?> FollowageAsync(LookupCall call, string login, string channel)
    {
        if (!call.IsOwnChannel(channel.Trim().TrimStart('@')))
            return null;

        if (await call.Data.UserAsync(login, call.Ct) is not { } user)
            return call.Fallback(LookupFallbacks.Twitch);

        if (await call.Data.FollowAsync(user.Id, call.Ct) is not { } follow)
            return call.Fallback(LookupFallbacks.Twitch);

        return follow.Follower is { } record
            ? StreamText.AgeInWords(record.FollowedAt, call.Scope.Now, call.Culture)
            : "";
    }
}
