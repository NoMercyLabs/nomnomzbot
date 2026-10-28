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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>
/// The fetch groups of the stream and channel helpers (<c>TemplateHelperEntry.Source</c>): rows with the same
/// name share one fetch per render.
/// </summary>
internal static class StreamSources
{
    public const string Stats = "stream.stats";
    public const string Recent = "stream.recent";
    public const string History = "stream.history";
    public const string Games = "stream.games";
    public const string ChannelInfo = "channel.info";
    public const string Followers = "channel.followers";
    public const string Subscribers = "channel.subscribers";
    public const string Teams = "channel.teams";
    public const string Staff = "channel.staff";
    public const string Videos = "channel.videos";
    public const string Raids = "channel.raids";
    public const string Owner = "channel.owner";
}
