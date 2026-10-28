<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The embed as Discord shows it: author line, linked title, text, fields, picture, footer with the time and the
// link button. It draws what the bot rendered, so the streamer sees the real words and links. The stripe takes
// the embed's own color through a CSS variable set from that data; a missing or malformed color falls back to
// the border token.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { DiscordEmbed } from '@/api/discord';
import { Button } from '@/components/ui/button';

const props = defineProps<{ embed: DiscordEmbed }>();

const { t } = useI18n();

const hexColor = /^#[0-9a-f]{6}$/i;

const stripe = computed(() =>
  props.embed.color && hexColor.test(props.embed.color)
    ? { '--embed-color': props.embed.color }
    : undefined);

const fields = computed(() => props.embed.fields ?? []);
const hasFooter = computed<boolean>(() => !!props.embed.footerText || !!props.embed.showTimestamp);
</script>

<template>
  <div
    data-preview-embed
    :style="stripe"
    class="flex flex-col gap-2 rounded-[4px] border-l-4 border-l-(--embed-color,var(--border)) bg-card p-3"
  >
    <div v-if="embed.thumbnailUrl" class="flex justify-end">
      <img
        :src="embed.thumbnailUrl"
        :alt="t('dashboard.discord.editor.previewThumbnail')"
        data-preview-thumbnail
        class="size-16 rounded-[2px] object-cover"
      >
    </div>

    <p v-if="embed.authorName" data-preview-author class="flex items-center gap-2 text-xs font-medium">
      <img
        v-if="embed.authorIconUrl"
        :src="embed.authorIconUrl"
        :alt="t('dashboard.discord.editor.previewAuthorIcon')"
        class="size-5 rounded-full object-cover"
      >
      <a
        v-if="embed.authorUrl"
        :href="embed.authorUrl"
        target="_blank"
        rel="noopener noreferrer"
        class="hover:underline"
      >{{ embed.authorName }}</a>
      <span v-else>{{ embed.authorName }}</span>
    </p>

    <p v-if="embed.title" data-preview-title class="font-medium">
      <a
        v-if="embed.titleUrl"
        :href="embed.titleUrl"
        target="_blank"
        rel="noopener noreferrer"
        class="underline-offset-2 hover:underline"
      >{{ embed.title }}</a>
      <span v-else>{{ embed.title }}</span>
    </p>

    <p v-if="embed.description">
      {{ embed.description }}
    </p>

    <dl v-if="fields.length > 0" class="grid grid-cols-3 gap-2">
      <div
        v-for="(field, index) in fields"
        :key="index"
        data-preview-field
        :class="field.inline
          ? 'col-span-1'
          : 'col-span-3'"
      >
        <dt class="text-xs font-medium">
          {{ field.name }}
        </dt>
        <dd class="text-sm">
          {{ field.value }}
        </dd>
      </div>
    </dl>

    <img
      v-if="embed.imageUrl"
      :src="embed.imageUrl"
      :alt="t('dashboard.discord.editor.previewImage')"
      data-preview-image
      class="w-full rounded-[2px] object-cover"
    >

    <p v-if="hasFooter" data-preview-footer class="flex items-center gap-2 text-xs text-muted-foreground">
      <img
        v-if="embed.footerIconUrl && embed.footerText"
        :src="embed.footerIconUrl"
        alt=""
        class="size-4 rounded-full object-cover"
      >
      <span v-if="embed.footerText">{{ embed.footerText }}</span>
      <span v-if="embed.showTimestamp" data-preview-timestamp>
        {{ t('dashboard.discord.editor.previewTimestamp') }}
      </span>
    </p>

    <Button
      v-if="embed.buttonLabel && embed.buttonUrl"
      type="button"
      variant="secondary"
      size="sm"
      tabindex="-1"
      aria-disabled="true"
      data-preview-button-link
      class="pointer-events-none self-start"
    >
      {{ embed.buttonLabel }}
    </Button>
  </div>
</template>
