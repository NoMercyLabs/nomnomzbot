<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The channel's live stream in the NoMercy video player, built the way the media share overlay builds it, with the
// player's own controls. Live plays through the bot's playlist proxy: only the playlists pass through it, the video
// loads from Twitch's CDN. An offline or ended channel shows the player's own message. It starts muted and never
// unmutes by code: playing your own stream back on the streaming PC can loop its sound into the stream.
import nmplayer from '@nomercy-entertainment/nomercy-video-player';
import { DesktopUiPlugin } from '@nomercy-entertainment/nomercy-video-player/plugins/desktop-ui';
import { TwitchPlugin } from '@nomercy-entertainment/nomercy-video-player/plugins/twitch';
import { onBeforeUnmount, onMounted, useId } from 'vue';

import { servedOrigin } from '@/lib/botAddress';

const props = defineProps<{
  login: string;
  title: string;
}>();

const containerId = `nnz-stream-player-${useId()}`;
const proxyUrl = `${servedOrigin}/api/v1/stream/playlist`;

let player: ReturnType<typeof nmplayer> | null = null;

onMounted(() => {
  const live = {
    id: props.login,
    title: props.title,
    url: `https://www.twitch.tv/${props.login}`,
  };

  player = nmplayer(containerId)
    .addPlugin(TwitchPlugin, { proxyUrl })
    .addPlugin(DesktopUiPlugin)
    .setup({
      muted: true,
      autoPlay: true,
      playlist: [live],
    });
});

onBeforeUnmount(() => {
  void player?.dispose();
  player = null;
});
</script>

<template>
  <div data-stream-player class="aspect-video w-full overflow-hidden rounded-xl bg-muted">
    <div :id="containerId" class="size-full" />
  </div>
</template>
