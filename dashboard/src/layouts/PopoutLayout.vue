<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A popped-out page in its own window: the frame's live connection, accent, tooltips, toasts and profile
// dialog, without the sidebar.
import { watchEffect } from 'vue';

import ProfileDialog from '@/components/layout/ProfileDialog.vue';
import { Toaster } from '@/components/ui/sonner';
import { TooltipProvider } from '@/components/ui/tooltip';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { applyAccent, defaultAccent } from '@/lib/accent';
import { useRealtime } from '@/realtime/useRealtime';

const { current, channelId } = useCurrentChannel();

watchEffect(() => applyAccent(current.value?.chatColor ?? defaultAccent));

useRealtime();
</script>

<template>
  <TooltipProvider :delay-duration="300">
    <div class="flex h-svh overflow-hidden bg-background text-foreground">
      <main class="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden">
        <RouterView />
      </main>
      <ProfileDialog v-if="channelId" :key="channelId" />
    </div>
    <Toaster />
  </TooltipProvider>
</template>
