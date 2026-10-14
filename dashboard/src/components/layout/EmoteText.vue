<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Stored chat text the bot keeps without fragments, such as a profile's latest lines. Every word that is an emote
// code usable in this channel shows as that emote, the way chat showed it; every other word stays plain text.
import { computed } from 'vue';

import { useChatEmotes, type ChatEmoteEntry } from '@/api/chat';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { emoteFrames, smallestUrl } from '@/lib/assetCards';

type Part = { text: string; src?: string };

const props = defineProps<{ text: string }>();

const { channelId } = useCurrentChannel();
const emotes = useChatEmotes(channelId);

const byCode = computed(() =>
  new Map((emotes.data.value ?? []).map((emote: ChatEmoteEntry) =>
    [emote.code, emote])));

const parts = computed<Part[]>(() =>
  props.text.split(/(\s+)/u).filter((word: string) =>
    word !== '').map((word: string) => {
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
