<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { useI18n } from 'vue-i18n';

import {
  SidebarGroupContent,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from '@/components/ui/sidebar';
import type { NavEntry } from './navigation';
import { useNavigation } from './useNavigation';

defineProps<{ entries: readonly NavEntry[] }>();

const { t } = useI18n();
const { isCurrent, linkTo } = useNavigation();
</script>

<template>
  <SidebarGroupContent>
    <SidebarMenu>
      <SidebarMenuItem v-for="entry in entries" :key="entry.route">
        <SidebarMenuButton as-child :is-active="isCurrent(entry)">
          <RouterLink :to="linkTo(entry)">
            <component :is="entry.icon" aria-hidden="true" />
            <span>{{ t(`frame.nav.${entry.label}`) }}</span>
          </RouterLink>
        </SidebarMenuButton>
      </SidebarMenuItem>
    </SidebarMenu>
  </SidebarGroupContent>
</template>
