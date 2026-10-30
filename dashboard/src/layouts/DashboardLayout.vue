<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { computed, provide, watchEffect } from 'vue';

import { frameActionsKey } from '@/components/layout/frameActions';
import ProfileDialog from '@/components/layout/ProfileDialog.vue';
import { Toaster } from '@/components/ui/sonner';
import { TooltipProvider } from '@/components/ui/tooltip';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { railWanted } from '@/composables/useFrameRail';
import { useIntentPrefetch } from '@/composables/useIntentPrefetch';
import { useInviteOutcome } from '@/composables/useInviteOutcome';
import { useScreenFrom } from '@/composables/useLayout';
import { useRouteChannel } from '@/composables/useRouteChannel';
import { applyAccent, defaultAccent } from '@/lib/accent';
import { useRealtime } from '@/realtime/useRealtime';

import BottomBar from './dashboard/BottomBar.vue';
import ChannelNoAccess from './dashboard/ChannelNoAccess.vue';
import ChannelSuspended from './dashboard/ChannelSuspended.vue';
import ChannelSwitch from './dashboard/ChannelSwitch.vue';
import DashboardSidebar from './dashboard/DashboardSidebar.vue';
import FrameActions from './dashboard/FrameActions.vue';
import NavRail from './dashboard/NavRail.vue';
import ProfileMenu from './dashboard/ProfileMenu.vue';
import ShortcutsDialog from './dashboard/ShortcutsDialog.vue';
import { useReadableRedirect } from './dashboard/useReadableRedirect';
import ViewAsBanner from './dashboard/ViewAsBanner.vue';

// Three frames (spec frontend-vue-components.md section 2): sidebar from 840 px, icon rail from 600 px, bottom bar below.
const wide = useScreenFrom('sidebar');
const medium = useScreenFrom('rail');

const { current, channels, channelId } = useCurrentChannel();
const { name: channelName, denied } = useRouteChannel();

// A suspended channel closes the whole frame: no navigation leads anywhere while it lasts.
const suspension = computed(() => current.value?.suspension ?? null);
const canSwitch = computed(() => channels.value.length > 1);

// The theme wears the chat color of the channel being managed; the approved pink stands in without one.
watchEffect(() => applyAccent(current.value?.chatColor ?? defaultAccent));

provide(frameActionsKey, FrameActions);
useRealtime();
useIntentPrefetch();
useInviteOutcome();
useReadableRedirect();
</script>

<template>
  <!-- The one tooltip provider and toast host of the frame; pages never mount their own. -->
  <TooltipProvider :delay-duration="300">
    <!-- C1: from the tablet width up the window never scrolls; only a panel body does, so every box here clips.
         Below it the page is one document scroll (owner rule 2026-10-21): the frame header and the page header stay
         on top, the action footer and the bottom bar stay at the bottom, and no panel scrolls inside itself. -->
    <div
      data-slot="frame"
      class="flex h-svh overflow-hidden bg-background text-foreground max-md:h-auto max-md:min-h-svh max-md:overflow-visible"
    >
      <template v-if="!suspension">
        <DashboardSidebar v-if="wide && !railWanted" />
        <NavRail v-else-if="medium" class="max-md:sticky max-md:top-0" />
      </template>
      <div class="flex min-h-0 min-w-0 flex-1 flex-col">
        <ViewAsBanner />
        <!-- Without the sidebar, the channel switch and the profile need a home of their own. -->
        <header
          v-if="!wide && !suspension"
          data-slot="frame-header"
          class="flex h-12 flex-none items-center gap-2 border-b border-border px-3 max-md:sticky max-md:top-0 max-md:z-30 max-md:bg-background"
        >
          <div class="w-48 min-w-0">
            <ChannelSwitch compact />
          </div>
          <div class="ml-auto">
            <ProfileMenu icon />
          </div>
        </header>
        <!-- Below the tablet width the page header (a page's own <header>) sticks under the frame header. -->
        <main
          class="flex min-h-0 flex-1 flex-col overflow-hidden max-md:overflow-visible max-md:[&>*>header]:sticky max-md:[&>*>header]:top-12 max-md:[&>*>header]:z-20 max-md:[&>*>header]:bg-background max-md:[&>header]:sticky max-md:[&>header]:top-12 max-md:[&>header]:z-20 max-md:[&>header]:bg-background"
        >
          <ChannelSuspended
            v-if="suspension && current"
            :name="current.displayName"
            :reason="suspension.reason ?? null"
            :suspended-at="suspension.suspendedAt ?? null"
            :contact-url="suspension.contactUrl ?? null"
            :can-switch="canSwitch"
          />
          <ChannelNoAccess v-else-if="denied && channelName" :name="channelName" />
          <RouterView v-else />
        </main>
        <div class="flex-none max-md:sticky max-md:bottom-0 max-md:z-30">
          <!-- The one action footer slot: a page's PageActionFooter moves here below the tablet width. -->
          <div data-slot="frame-action-footer" />
          <BottomBar v-if="!medium && !suspension" />
        </div>
      </div>
      <!-- The one profile dialog of the frame; every UserName opens it. -->
      <ProfileDialog v-if="channelId" :key="channelId" />
      <ShortcutsDialog />
    </div>
    <Toaster />
  </TooltipProvider>
</template>
