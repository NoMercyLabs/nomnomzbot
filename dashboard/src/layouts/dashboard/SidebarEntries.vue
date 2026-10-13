<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
const { isCurrent } = useNavigation();
</script>

<template>
  <SidebarGroupContent>
    <SidebarMenu>
      <SidebarMenuItem v-for="entry in entries" :key="entry.route">
        <SidebarMenuButton as-child :is-active="isCurrent(entry)">
          <RouterLink :to="{ name: entry.route }">
            <component :is="entry.icon" aria-hidden="true" />
            <span>{{ t(`frame.nav.${entry.label}`) }}</span>
          </RouterLink>
        </SidebarMenuButton>
      </SidebarMenuItem>
    </SidebarMenu>
  </SidebarGroupContent>
</template>
