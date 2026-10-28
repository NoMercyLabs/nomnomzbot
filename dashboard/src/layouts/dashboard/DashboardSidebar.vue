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
import ChevronDown from '~icons/nnz/chevron-down';

import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarProvider,
} from '@/components/ui/sidebar';
import ChannelSwitch from './ChannelSwitch.vue';
import ProfileMenu from './ProfileMenu.vue';
import SidebarEntries from './SidebarEntries.vue';
import { useNavigation } from './useNavigation';

const { t } = useI18n();
const { groups, toggle } = useNavigation();
</script>

<template>
  <!-- The sidebar is plain here (collapsible none): the frame shows it from 840 px only, below that it shows the rail or the bottom bar. -->
  <SidebarProvider class="min-h-0 w-auto">
    <Sidebar collapsible="none" class="h-svh border-r border-sidebar-border">
      <SidebarHeader>
        <ChannelSwitch />
      </SidebarHeader>
      <SidebarContent>
        <template v-for="group in groups" :key="group.key">
          <Collapsible
            v-if="group.foldable"
            as-child
            :open="group.open"
            @update:open="toggle(group)"
          >
            <SidebarGroup>
              <SidebarGroupLabel as-child>
                <CollapsibleTrigger :disabled="group.holdsOpenPage" class="w-full justify-between">
                  {{ t(`frame.group.${group.key}`) }}
                  <ChevronDown
                    class="size-3.5 transition-transform"
                    :class="group.open ? '' : '-rotate-90'"
                    aria-hidden="true"
                  />
                </CollapsibleTrigger>
              </SidebarGroupLabel>
              <CollapsibleContent>
                <SidebarEntries :entries="group.entries" />
              </CollapsibleContent>
            </SidebarGroup>
          </Collapsible>
          <SidebarGroup v-else>
            <SidebarEntries :entries="group.entries" />
          </SidebarGroup>
        </template>
      </SidebarContent>
      <SidebarFooter>
        <ProfileMenu />
      </SidebarFooter>
    </Sidebar>
  </SidebarProvider>
</template>
