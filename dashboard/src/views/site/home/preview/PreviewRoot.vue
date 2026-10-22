<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
