<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A popped-out page in its own window: the frame's live connection, accent, tooltips, toasts and profile
// dialog, without the sidebar.
import { watchEffect } from 'vue';

import ProfileDialog from '@/components/layout/ProfileDialog.vue';
import { Toaster } from '@/components/ui/sonner';
import { TooltipProvider } from '@/components/ui/tooltip';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useIntentPrefetch } from '@/composables/useIntentPrefetch';
import { applyAccent, defaultAccent } from '@/lib/accent';
import { useRealtime } from '@/realtime/useRealtime';

const { current, channelId } = useCurrentChannel();

watchEffect(() => applyAccent(current.value?.chatColor ?? defaultAccent));

useRealtime();
useIntentPrefetch();
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
