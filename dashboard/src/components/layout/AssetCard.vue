<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The card an emote or a badge opens on a click, in a chat line or a profile: the image at its largest size,
// its name and the facts it carries, as Twitch and FrankerFaceZ show them.
import { computed } from 'vue';

import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { merge } from '@/lib/array';
import type { AssetPerson } from '@/lib/assetCards';

const props = defineProps<{
  label: string;
  title: string;
  image: string;
  facts: readonly KeyValueItem[];
  people?: readonly AssetPerson[];
  link?: { href: string; label: string } | null;
}>();

const rows = computed(() =>
  merge(props.facts, props.people ?? []));

function personOf(item: KeyValueItem): AssetPerson | undefined {
  return props.people?.find((person: AssetPerson) =>
    person === item);
}
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
      <KeyValueList :items="rows">
        <template #value="{ item }">
          <UserName
            v-if="personOf(item)?.userId"
            :data-asset-person="personOf(item)!.role"
            :user-id="personOf(item)!.userId!"
            :name="String(item.value)"
          />
          <a
            v-else-if="personOf(item)?.href"
            :data-asset-person="personOf(item)!.role"
            :href="personOf(item)!.href!"
            target="_blank"
            rel="noopener noreferrer"
            class="font-medium underline-offset-4 hover:underline"
          >{{ item.value }}</a>
          <template v-else>{{ item.value }}</template>
        </template>
      </KeyValueList>
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
