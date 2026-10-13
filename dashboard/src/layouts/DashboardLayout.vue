<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import { provide } from 'vue'

import { frameActionsKey } from '@/components/layout/frameActions'
import ProfileDialog from '@/components/layout/ProfileDialog.vue'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

import BottomBar from './dashboard/BottomBar.vue'
import ChannelSwitch from './dashboard/ChannelSwitch.vue'
import DashboardSidebar from './dashboard/DashboardSidebar.vue'
import FrameActions from './dashboard/FrameActions.vue'
import NavRail from './dashboard/NavRail.vue'
import ProfileMenu from './dashboard/ProfileMenu.vue'

// Three frames (spec frontend-vue-components.md section 2): sidebar from 840 px, icon rail from 600 px, bottom bar below.
const wide = useMediaQuery('(min-width: 840px)')
const medium = useMediaQuery('(min-width: 600px)')

const { channelId } = useCurrentChannel()

provide(frameActionsKey, FrameActions)
</script>

<template>
  <!-- C1: the window never scrolls. Only a panel body scrolls, so every box here clips. -->
  <div class="flex h-svh overflow-hidden bg-background text-foreground">
    <DashboardSidebar v-if="wide" />
    <NavRail v-else-if="medium" />
    <div class="flex min-h-0 min-w-0 flex-1 flex-col">
      <!-- Without the sidebar, the channel switch and the profile need a home of their own. -->
      <header
        v-if="!wide"
        data-slot="frame-header"
        class="flex h-12 flex-none items-center gap-2 border-b border-border px-3"
      >
        <div class="w-48 min-w-0">
          <ChannelSwitch compact />
        </div>
        <div class="ml-auto">
          <ProfileMenu icon />
        </div>
      </header>
      <main class="flex min-h-0 flex-1 flex-col overflow-hidden">
        <RouterView />
      </main>
      <BottomBar v-if="!medium" />
    </div>
    <!-- The one profile dialog of the frame; every UserName opens it. -->
    <ProfileDialog v-if="channelId" :key="channelId" />
  </div>
</template>
