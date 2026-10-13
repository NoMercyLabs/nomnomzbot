<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { ChevronDown } from '@lucide/vue'
import { useI18n } from 'vue-i18n'

import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarProvider,
} from '@/components/ui/sidebar'
import ChannelSwitch from './ChannelSwitch.vue'
import ProfileMenu from './ProfileMenu.vue'
import SidebarEntries from './SidebarEntries.vue'
import { useNavigation } from './useNavigation'

const { t } = useI18n()
const { groups, toggle } = useNavigation()
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
          <Collapsible v-if="group.foldable" as-child :open="group.open" @update:open="toggle(group)">
            <SidebarGroup>
              <SidebarGroupLabel as-child>
                <CollapsibleTrigger :disabled="group.holdsOpenPage" class="w-full justify-between">
                  {{ t(`frame.group.${group.key}`) }}
                  <ChevronDown class="size-4 transition-transform" :class="group.open ? '' : '-rotate-90'" aria-hidden="true" />
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
