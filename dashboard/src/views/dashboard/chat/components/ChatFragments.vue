<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one renderer of a message's fragments: emote images, posted html, mentions and plain words.
// A chat line and a redemption's input both draw through it, so the same words look the same.
// The parent sets --emote-h and --line-h (see emoteVars).
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ChatLine } from '@/api/chat';
import AssetCardPopover from '@/components/layout/AssetCard.vue';
import {
  emoteCard,
  emoteFrames,
  smallestUrl,
  type AssetCard,
  type EmoteImages,
} from '@/lib/assetCards';
import { purifyChatHtml } from '@/lib/html';

import { shownNameColor, splitMentions, type ChatDisplay } from '../chatDisplay';

const props = defineProps<{
  fragments: ChatLine['fragments'];
  display: ChatDisplay;
  me: string | null;
}>();

const emit = defineEmits<{
  image: [event: MouseEvent];
}>();

const { t } = useI18n();

function parts(text: string): ReturnType<typeof splitMentions> {
  const me = props.display.mentions
    ? props.me
    : null;

  return splitMentions(text, me);
}

// One card per emote fragment, by its place in the line; a fragment with no emote image has none.
const emoteCards = computed<(AssetCard | null)[]>(() =>
  props.fragments.map((fragment) =>
    emoteCard(fragment, t)));

function emoteUrl(emote: EmoteImages): string | undefined {
  return smallestUrl(emoteFrames(emote));
}
</script>

<template>
  <template v-for="(fragment, at) in fragments" :key="at">
    <AssetCardPopover
      v-if="fragment.emote && emoteUrl(fragment.emote) && emoteCards[at]"
      :label="$t('asset.emoteLabel', { name: fragment.text })"
      :title="emoteCards[at]!.title"
      :image="emoteCards[at]!.image"
      :facts="emoteCards[at]!.facts"
      :people="emoteCards[at]!.people"
      :link="emoteCards[at]!.link"
      class="mx-0.5 my-[calc((var(--line-h)-var(--emote-h))/2)]"
    >
      <img
        data-emote
        :src="emoteUrl(fragment.emote)"
        :alt="fragment.text"
        class="inline-block h-(--emote-h)"
      />
    </AssetCardPopover>
    <span
      v-else-if="fragment.type === 'html' && display.htmlAsText"
      data-html
    >{{ fragment.text }}</span>
    <span
      v-else-if="fragment.type === 'html'"
      data-html
      class="[&_.emote]:mx-0.5 [&_.emote]:my-[calc((var(--line-h)-var(--emote-h))/2)] [&_.emote]:inline-block [&_.emote]:h-(--emote-h) [&_.emote]:align-top [&_img]:max-h-32 [&_img:not(.emote)]:cursor-zoom-in"
      @click="emit('image', $event)"
      v-html="purifyChatHtml(fragment.text)"
    />
    <span
      v-else-if="fragment.type === 'mention' && fragment.mention"
      data-mention-tag
      class="font-bold"
      :style="{ color: shownNameColor(fragment.mention.color, display.nameColors) }"
    >{{ fragment.text }}</span>
    <template v-else>
      <template v-for="(part, slot) in parts(fragment.text)" :key="slot">
        <mark v-if="part.mention" data-mention class="bg-transparent font-bold text-foreground">{{ part.text }}</mark>
        <span v-else-if="part.at" data-mention-tag class="font-bold">{{ part.text }}</span>
        <template v-else>{{ part.text }}</template>
      </template>
    </template>
  </template>
</template>
