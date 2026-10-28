<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one slim header of the docked chat (a phone, the popout, an OBS dock): title, status, the frame's
// shield and inbox, search and the three panel toggles. The feed keeps every other pixel of the height.
// From 416 px of header width the three panel toggles show as icons; narrower, they fold into one menu.
import { inject, type Component } from 'vue';
import Cross from '~icons/nnz/cross';
import MenuDotsHorizontal from '~icons/nnz/menu-dots-horizontal';
import SearchMagnifyingGlass from '~icons/nnz/search-magnifying-glass';
import Settings from '~icons/nnz/settings';
import SettingsFader from '~icons/nnz/settings-fader';
import UserCircle from '~icons/nnz/user-circle';

import { frameActionsKey } from '@/components/layout/frameActions';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

defineProps<{
  title: string;
  live: boolean;
  statusLabel: string;
  countLabel: string;
  clearFocusLabel: string | null;
  showFrameActions: boolean;
  // A viewer has no moderator tools and no sending identity to open.
  viewer?: boolean;
}>();

const searchOpen = defineModel<boolean>('searchOpen', { required: true });
const modToolsOpen = defineModel<boolean>('modToolsOpen', { required: true });
const identityOpen = defineModel<boolean>('identityOpen', { required: true });
const displayOpen = defineModel<boolean>('displayOpen', { required: true });

defineEmits<{ clearFocus: [] }>();

const frameActions: Component | null = inject(frameActionsKey, null);
</script>

<template>
  <header
    data-dock-header
    class="@container flex min-h-12 flex-none items-center gap-2 border-b border-border px-3"
  >
    <h1 class="min-w-0 truncate text-base font-semibold tracking-tight" :title="countLabel">
      {{ title }}
    </h1>
    <StatusBadge :tone="live ? 'live' : 'off'" :label="statusLabel" />
    <div class="ml-auto flex items-center gap-1">
      <Button
        v-if="clearFocusLabel !== null"
        data-focus-clear
        variant="ghost"
        size="icon"
        :aria-label="clearFocusLabel"
        @click="$emit('clearFocus')"
      >
        <Cross class="size-5" aria-hidden="true" />
      </Button>
      <component :is="frameActions" v-if="frameActions && showFrameActions" />
      <Button
        data-dock-search-toggle
        variant="ghost"
        size="icon"
        :aria-pressed="searchOpen"
        :aria-label="$t('dashboard.chat.dock.search')"
        @click="searchOpen = !searchOpen"
      >
        <SearchMagnifyingGlass class="size-5" aria-hidden="true" />
      </Button>
      <div class="hidden items-center gap-1 @md:flex">
        <Button
          v-if="!viewer"
          data-mod-tools-toggle
          variant="ghost"
          size="icon"
          :aria-pressed="modToolsOpen"
          :aria-label="$t('dashboard.chat.modTools.title')"
          @click="modToolsOpen = !modToolsOpen"
        >
          <Settings class="size-5" aria-hidden="true" />
        </Button>
        <Button
          v-if="!viewer"
          data-identity-toggle
          variant="ghost"
          size="icon"
          :aria-pressed="identityOpen"
          :aria-label="$t('dashboard.chat.identity.title')"
          @click="identityOpen = !identityOpen"
        >
          <UserCircle class="size-5" aria-hidden="true" />
        </Button>
        <Button
          data-display-toggle
          variant="ghost"
          size="icon"
          :aria-pressed="displayOpen"
          :aria-label="$t('dashboard.chat.display.toggle')"
          @click="displayOpen = !displayOpen"
        >
          <SettingsFader class="size-5" aria-hidden="true" />
        </Button>
      </div>
      <DropdownMenu>
        <DropdownMenuTrigger as-child>
          <Button
            data-dock-more
            class="@md:hidden"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.chat.dock.more')"
          >
            <MenuDotsHorizontal class="size-5" aria-hidden="true" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuCheckboxItem
            v-if="!viewer"
            v-model="modToolsOpen"
            data-more-mod-tools
            opens-panel
          >
            <Settings aria-hidden="true" />
            {{ $t('dashboard.chat.modTools.title') }}
          </DropdownMenuCheckboxItem>
          <DropdownMenuCheckboxItem
            v-if="!viewer"
            v-model="identityOpen"
            data-more-identity
            opens-panel
          >
            <UserCircle aria-hidden="true" />
            {{ $t('dashboard.chat.identity.title') }}
          </DropdownMenuCheckboxItem>
          <DropdownMenuCheckboxItem v-model="displayOpen" data-more-display opens-panel>
            <SettingsFader aria-hidden="true" />
            {{ $t('dashboard.chat.display.toggle') }}
          </DropdownMenuCheckboxItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  </header>
</template>
