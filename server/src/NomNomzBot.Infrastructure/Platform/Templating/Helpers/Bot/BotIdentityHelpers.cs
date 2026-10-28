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

using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

/// <summary>Who the bot is: its name, build, licence and whether it can moderate.</summary>
internal sealed class BotIdentityHelpers()
    : BotHelperGroup(
        BotRow.Read("bot.name", "SampleBot", BotSources.Identity, call => NameAsync(call, "")),
        BotRow.Read("bot.mention", "@SampleBot", BotSources.Identity, call => NameAsync(call, "@")),
        BotRow.Memory("bot.version", "1.42.0", call => call.Data.Process.Version),
        BotRow.Fixed("bot.license", "PolyForm-Noncommercial-1.0.0", BotText.License),
        BotRow.Fixed("bot.sourceCode", BotText.SourceCode, BotText.SourceCode),
        BotRow.Read("bot.isMod", "true", BotSources.Moderator, IsModAsync)
    )
{
    private static async Task<string?> NameAsync(BotCall call, string lead) =>
        lead + await call.Data.NameAsync();

    /// <summary>
    /// <c>true</c> when the bot that speaks here holds the moderator role (or the streamer's own account speaks,
    /// which needs none), <c>false</c> when it does not, and nothing while no status was ever recorded.
    /// </summary>
    private static async Task<string?> IsModAsync(BotCall call) =>
        call.Channel is null
            ? null
            : await call.Data.ModeratorAsync() switch
            {
                BotModeratorStanding.Moderator or BotModeratorStanding.NoDedicatedBot => "true",
                BotModeratorStanding.NotModerator => "false",
                _ => BotText.Nothing,
            };
}
