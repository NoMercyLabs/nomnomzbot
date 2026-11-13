<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The More tab of the viewer phone bar: the pages the bar has no room for open in a bottom drawer, one full-width
// row each, as the dashboard bottom bar does. The viewer has one flat list, so there are no group headings.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import ChevronRight from '~icons/nnz/chevron-right';
import Cross from '~icons/nnz/cross';
import MenuDotsHorizontal from '~icons/nnz/menu-dots-horizontal';

import { Button } from '@/components/ui/button';
import {
  Drawer,
  DrawerContent,
  DrawerDescription,
  DrawerHeader,
  DrawerTitle,
  DrawerTrigger,
} from '@/components/ui/drawer';
import { Item, ItemActions, ItemContent, ItemGroup, ItemMedia, ItemTitle } from '@/components/ui/item';

import type { ViewerNavEntry } from './viewerNavigation';

defineProps<{
  entries: readonly ViewerNavEntry[];
  channel: string;
  current: string;
}>();

const { t } = useI18n();
const open = ref(false);
</script>

<template>
  <Drawer v-model:open="open">
    <DrawerTrigger as-child>
      <Button variant="ghost" class="flex-1 h-auto! min-h-11 min-w-0 flex-col gap-1! px-1! py-1.5 text-xs no-underline!">
        <MenuDotsHorizontal class="size-5" aria-hidden="true" />
        <span class="min-w-0 max-w-full truncate text-center leading-tight">{{ t('frame.nav.more') }}</span>
      </Button>
    </DrawerTrigger>
    <DrawerContent data-slot="more-drawer">
      <DrawerHeader class="flex-row items-center justify-between">
        <DrawerTitle>{{ t('frame.nav.more') }}</DrawerTitle>
        <DrawerDescription class="sr-only">{{ t('frame.nav.moreHelp') }}</DrawerDescription>
        <Button
          data-drawer-close
          variant="ghost"
          size="icon"
          :aria-label="t('common.close')"
          @click="open = false"
        >
          <Cross class="size-5" aria-hidden="true" />
        </Button>
      </DrawerHeader>
      <ItemGroup class="overflow-y-auto px-2 pb-4">
        <Item
          v-for="entry in entries"
          :key="entry.route"
          size="sm"
          :class="current === entry.route && 'bg-muted'"
          as-child
        >
          <RouterLink :to="{ name: entry.route, params: { channel } }" @click="open = false">
            <ItemMedia variant="icon">
              <component :is="entry.icon" aria-hidden="true" />
            </ItemMedia>
            <ItemContent>
              <ItemTitle>{{ t(`frame.nav.${entry.label}`) }}</ItemTitle>
            </ItemContent>
            <ItemActions>
              <ChevronRight class="size-4 text-muted-foreground" aria-hidden="true" />
            </ItemActions>
          </RouterLink>
        </Item>
      </ItemGroup>
    </DrawerContent>
  </Drawer>
</template>
