<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import nmplayer from '@nomercy-entertainment/nomercy-video-player';
import { DesktopUiPlugin } from '@nomercy-entertainment/nomercy-video-player/plugins/desktop-ui';
import { TwitchPlugin } from '@nomercy-entertainment/nomercy-video-player/plugins/twitch';
import { onBeforeUnmount, useId, watch } from 'vue';

import { servedOrigin } from '@/lib/botAddress';

const props = defineProps<{
  login: string;
  title: string;
  // The bot already knows when the channel is offline: no player is built for a stream that is not there.
  live: boolean;
  offlineLabel: string;
}>();

const containerId = `nnz-stream-player-${useId()}`;
const proxyUrl = `${servedOrigin}/api/v1/stream/playlist`;

let player: ReturnType<typeof nmplayer> | null = null;

function build(): void {
  const stream = {
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
      playlist: [stream],
    });
}

function dispose(): void {
  void player?.dispose();
  player = null;
}

watch(() =>
  props.live, (live) => {
  dispose();

  if (live) {
    build();
  }
}, { immediate: true, flush: 'post' });

onBeforeUnmount(dispose);
</script>

<template>
  <div data-stream-player class="aspect-video w-full overflow-hidden rounded-xl bg-muted">
    <div v-if="live" :id="containerId" class="size-full" />
    <div v-else data-stream-offline class="flex size-full items-center justify-center p-4 text-center text-sm text-muted-foreground">
      {{ offlineLabel }}
    </div>
  </div>
</template>
