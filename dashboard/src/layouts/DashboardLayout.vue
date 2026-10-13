<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'

import BottomBar from './dashboard/BottomBar.vue'
import ChannelSwitch from './dashboard/ChannelSwitch.vue'
import DashboardSidebar from './dashboard/DashboardSidebar.vue'
import ErrorInbox from './dashboard/ErrorInbox.vue'
import NavRail from './dashboard/NavRail.vue'
import ProfileMenu from './dashboard/ProfileMenu.vue'
import ShieldToggle from './dashboard/ShieldToggle.vue'

// Three frames (spec frontend-vue-components.md section 2): sidebar from 840 px, icon rail from 600 px, bottom bar below.
const wide = useMediaQuery('(min-width: 840px)')
const medium = useMediaQuery('(min-width: 600px)')
</script>

<template>
  <!-- C1: the window never scrolls. Only a panel body scrolls, so every box here clips. -->
  <div class="flex h-svh overflow-hidden bg-background text-foreground">
    <DashboardSidebar v-if="wide" />
    <NavRail v-else-if="medium" />
    <div class="flex min-h-0 min-w-0 flex-1 flex-col">
      <header data-slot="frame-header" class="flex h-12 flex-none items-center gap-2 border-b border-border px-3">
        <div v-if="!wide" class="w-48 min-w-0">
          <ChannelSwitch compact />
        </div>
        <div class="ml-auto flex items-center gap-2">
          <ShieldToggle />
          <ErrorInbox />
          <ProfileMenu v-if="!wide" icon />
        </div>
      </header>
      <main class="flex min-h-0 flex-1 flex-col overflow-hidden">
        <RouterView />
      </main>
      <BottomBar v-if="!medium" />
    </div>
  </div>
</template>
