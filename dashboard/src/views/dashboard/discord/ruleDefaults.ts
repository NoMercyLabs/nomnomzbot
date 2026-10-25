// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { DiscordChannel, DiscordEmbed } from '@/api/discord';

type ReadyWords = {
  author: string;
  game: string;
  button: string;
};

// The embed a new go-live rule starts with: the author line with the streamer's picture, the stream title
// linking to the channel, the game, the live preview picture, the send time and a Watch Stream button. The
// placeholders are the bot's own; only the visible words come from the translations.
export function readyGoLiveEmbed(words: ReadyWords): DiscordEmbed {
  return {
    title: '{title}',
    titleUrl: '{channel.url}',
    authorName: words.author,
    authorIconUrl: '{channel.avatar}',
    imageUrl: '{stream.thumbnail}',
    color: '#9146FF',
    fields: [
      {
        name: words.game,
        value: '{game}',
        inline: true,
      },
    ],
    showTimestamp: true,
    buttonLabel: words.button,
    buttonUrl: '{channel.url}',
  };
}

// Discord channel types a notification can go to: 0 is text, 5 is announcement.
const textTypes = new Set<string | number>([0, 5]);

const liveNames = /live|stream|announce|notif/i;
const alertNames = /follow|sub|gift|cheer|bits|raid|alert|activity|stream|announce|notif/i;
const generalNames = /general|chat|main/i;

// A person new to the page should not have to hunt for a channel: the rule starts on the channel whose name fits
// the event. A channel the bot can post in wins; one it cannot is still picked when nothing else fits, since the
// bot raises its own access there.
export function defaultChannelFor(trigger: string, channels: DiscordChannel[]): string {
  const fits = trigger === 'go_live'
    ? liveNames
    : alertNames;
  const text = channels.filter((channel) => textTypes.has(channel.type));
  const byPost = [...text].sort((a, b) => Number(b.canPost) - Number(a.canPost));
  const pick =
    byPost.find((channel) => channel.canPost && fits.test(channel.name ?? '')) ??
    byPost.find((channel) => channel.canPost && generalNames.test(channel.name ?? '')) ??
    byPost.find((channel) => fits.test(channel.name ?? '')) ??
    byPost[0];

  return pick?.id ?? '';
}
