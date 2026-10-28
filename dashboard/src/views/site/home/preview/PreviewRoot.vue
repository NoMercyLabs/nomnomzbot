<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The root of one preview app: the same frame the dashboard layout draws (tooltip host, sidebar, main area),
// without the live hub, the toasts and the dialogs, which a still preview has no use for.
import { TooltipProvider } from '@/components/ui/tooltip';
import DashboardSidebar from '@/layouts/dashboard/DashboardSidebar.vue';

defineProps<{ shell: boolean }>();
</script>

<template>
  <TooltipProvider :delay-duration="300">
    <div class="flex size-full overflow-hidden bg-background text-foreground">
      <DashboardSidebar v-if="shell" data-testid="preview-sidebar" />
      <main class="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden">
        <RouterView />
      </main>
    </div>
  </TooltipProvider>
</template>
