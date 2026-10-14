<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The card an emote or a badge in a chat line opens on a click: the image at its largest size, its name and
// the facts the line carries, as Twitch and FrankerFaceZ show them.
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';

defineProps<{
  label: string;
  title: string;
  image: string;
  facts: readonly KeyValueItem[];
  link?: { href: string; label: string } | null;
}>();
</script>

<template>
  <Popover>
    <PopoverTrigger
      data-asset-trigger
      :aria-label="label"
      class="inline-flex cursor-pointer rounded-sm align-top focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
    >
      <slot />
    </PopoverTrigger>
    <PopoverContent data-asset-card class="w-64" side="top">
      <img :src="image" :alt="title" class="mx-auto size-28 object-contain" />
      <p class="text-center font-medium break-all">{{ title }}</p>
      <KeyValueList :items="facts" />
      <Button
        v-if="link"
        as="a"
        :href="link.href"
        target="_blank"
        rel="noopener noreferrer"
        variant="outline"
        size="sm"
      >
        {{ link.label }}
      </Button>
    </PopoverContent>
  </Popover>
</template>
