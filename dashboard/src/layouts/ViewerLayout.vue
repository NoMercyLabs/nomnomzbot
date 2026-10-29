<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A signed-in viewer's pages: a plain frame with the brand, a finder for any channel and the language,
// and none of the management shell (no sidebar, no managed-channel switcher, no live management connection).
import AppMark from '@/components/layout/AppMark.vue';
import LanguagePicker from '@/components/layout/LanguagePicker.vue';
import { Toaster } from '@/components/ui/sonner';
import { TooltipProvider } from '@/components/ui/tooltip';

import ViewerNav from '@/views/viewer/nav/ViewerNav.vue';

import ViewAsBanner from './dashboard/ViewAsBanner.vue';
import ViewerChannelFinder from './dashboard/ViewerChannelFinder.vue';
import ViewerDashboardLink from './dashboard/ViewerDashboardLink.vue';
</script>

<template>
  <TooltipProvider :delay-duration="300">
    <div class="min-h-svh bg-background text-foreground">
      <ViewAsBanner />
      <!-- Below the tablet width the header stays on top while the page scrolls (owner rule 2026-10-21). -->
      <header
        class="mx-auto flex w-full max-w-4xl items-center justify-between px-6 py-4 max-md:sticky max-md:py-2 max-md:top-0 max-md:z-30 max-md:bg-background"
      >
        <span class="flex items-center gap-2 text-base font-semibold">
          <AppMark />
          <span class="max-sm:sr-only">{{ $t('frame.brand') }}</span>
        </span>
        <span class="flex items-center gap-2">
          <ViewerDashboardLink />
          <ViewerChannelFinder />
          <LanguagePicker />
        </span>
      </header>
      <div data-slot="viewer-nav-row" class="mx-auto w-full max-w-4xl px-6">
        <ViewerNav />
      </div>
      <main class="mx-auto flex w-full max-w-4xl flex-col gap-6 py-4 max-md:pb-24">
        <RouterView />
      </main>
    </div>
    <Toaster />
  </TooltipProvider>
</template>
