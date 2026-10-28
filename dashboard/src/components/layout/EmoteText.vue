<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Stored chat text the bot keeps without fragments, such as a profile's latest lines. Every word that is an emote
// code usable in this channel shows as that emote, the way chat showed it; every other word stays plain text.
import { computed } from 'vue';

import { useChatEmotes } from '@/api/chat';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { emoteFrames, smallestUrl } from '@/lib/assetCards';

type Part = { text: string; src?: string };

const props = defineProps<{ text: string }>();

const { channelId } = useCurrentChannel();
const emotes = useChatEmotes(channelId);

const byCode = computed(() =>
  new Map((emotes.data.value ?? []).map((emote) =>
    [emote.code, emote])));

const parts = computed<Part[]>(() =>
  props.text.split(/(\s+)/u).filter((word) =>
    word !== '').map((word) => {
    const emote = byCode.value.get(word);

    return {
      text: word,
      src: emote
        ? smallestUrl(emoteFrames(emote))
        : undefined,
    };
  }));
</script>

<template>
  <span data-slot="emote-text">
    <template v-for="(part, at) in parts" :key="at">
      <img
        v-if="part.src"
        :src="part.src"
        :alt="part.text"
        :title="part.text"
        :data-emote="part.text"
        class="inline-block h-5 max-w-none align-middle"
      >
      <template v-else>{{ part.text }}</template>
    </template>
  </span>
</template>
