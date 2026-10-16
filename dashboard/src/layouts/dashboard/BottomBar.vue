<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { computed, ref } from 'vue';
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
import { splitBottomBar, type BottomBarSplit } from './bottomBarSplit';
import type { NavEntry } from './navigation';
import { useNavigation } from './useNavigation';

const { t } = useI18n();
const { entries, isCurrent } = useNavigation();
const more = ref(false);

// The pages a thumb needs most stay in the bar; the rest open in the sheet.
const split = computed<BottomBarSplit>(() => splitBottomBar(entries.value));
const barEntries = computed<NavEntry[]>(() => split.value.bar);
const sheetEntries = computed<NavEntry[]>(() => split.value.sheet);
</script>

<template>
  <nav
    data-slot="bottom-bar"
    :aria-label="t('frame.nav.label')"
    class="flex flex-none items-stretch justify-around border-t border-sidebar-border bg-sidebar p-1 text-sidebar-foreground"
  >
    <Button
      v-for="entry in barEntries"
      :key="entry.route"
      :variant="isCurrent(entry) ? 'secondary' : 'ghost'"
      class="h-auto! min-h-11 flex-1 flex-col gap-1! px-1! py-1.5 text-xs"
      as-child
    >
      <RouterLink :to="{ name: entry.route }">
        <component :is="entry.icon" class="size-5" aria-hidden="true" />
        <span class="truncate">{{ t(`frame.nav.${entry.label}`) }}</span>
      </RouterLink>
    </Button>
    <!-- The rest opens in a bottom drawer, as the "More" tab of a phone app does: a handle to swipe it away,
         one full-width row per page. -->
    <Drawer v-if="sheetEntries.length" v-model:open="more">
      <DrawerTrigger as-child>
        <Button variant="ghost" class="h-auto! min-h-11 flex-1 flex-col gap-1! px-1! py-1.5 text-xs">
          <MenuDotsHorizontal aria-hidden="true" />
          <span class="truncate">{{ t('frame.nav.more') }}</span>
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
            @click="more = false"
          >
            <Cross class="size-5" aria-hidden="true" />
          </Button>
        </DrawerHeader>
        <ItemGroup class="overflow-y-auto px-2 pb-4">
          <Item
            v-for="entry in sheetEntries"
            :key="entry.route"
            size="sm"
            :class="isCurrent(entry) && 'bg-muted'"
            as-child
          >
            <RouterLink :to="{ name: entry.route }" @click="more = false">
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
  </nav>
</template>
