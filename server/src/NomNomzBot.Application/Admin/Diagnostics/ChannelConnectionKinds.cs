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

namespace NomNomzBot.Application.Admin.Diagnostics;

/// <summary>The values of <c>ChannelDiagnosticConnectionDto.Kind</c>: one per place the code base stores a connection.</summary>
public static class ChannelConnectionKinds
{
    /// <summary>A streaming platform link (<c>PlatformConnection</c>): Twitch, Kick, YouTube.</summary>
    public const string Platform = "platform";

    /// <summary>A vault connection with sealed tokens (<c>IntegrationConnection</c>): Twitch, Spotify, YouTube, Patreon, ...</summary>
    public const string Integration = "integration";

    /// <summary>A linked Discord server (<c>DiscordGuildConnection</c>).</summary>
    public const string DiscordGuild = "discordGuild";

    /// <summary>The OBS link (<c>ObsConnection</c>).</summary>
    public const string Obs = "obs";

    /// <summary>The VTube Studio link (<c>VtsConnection</c>).</summary>
    public const string Vts = "vts";

    /// <summary>An automation API token (<c>AutomationApiToken</c>).</summary>
    public const string AutomationToken = "automationToken";

    /// <summary>A supporter-event source such as a donation site (<c>SupporterConnection</c>).</summary>
    public const string Supporter = "supporter";

    /// <summary>An outbound or inbound webhook endpoint.</summary>
    public const string Webhook = "webhook";

    /// <summary>A legacy provider row (<c>Service</c>) that older integrations still write.</summary>
    public const string Service = "service";
}
