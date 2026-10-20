<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A signed-in viewer's pages: a plain frame with the brand, a finder for any channel and the language,
// and none of the management shell (no sidebar, no managed-channel switcher, no live management connection).
import AppMark from '@/components/layout/AppMark.vue';
import LanguagePicker from '@/components/layout/LanguagePicker.vue';
import { Toaster } from '@/components/ui/sonner';
import { TooltipProvider } from '@/components/ui/tooltip';

import ViewerNav from '@/views/viewer/nav/ViewerNav.vue';

import ViewerChannelFinder from './dashboard/ViewerChannelFinder.vue';
</script>

<template>
  <TooltipProvider :delay-duration="300">
    <div class="min-h-svh bg-background text-foreground">
      <header class="mx-auto flex w-full max-w-3xl items-center justify-between px-6 py-4">
        <span class="flex items-center gap-2 text-base font-semibold">
          <AppMark />
          {{ $t('frame.brand') }}
        </span>
        <span class="flex items-center gap-2">
          <ViewerChannelFinder />
          <LanguagePicker />
        </span>
      </header>
      <div data-slot="viewer-nav-row" class="mx-auto w-full max-w-3xl px-6">
        <ViewerNav />
      </div>
      <main class="mx-auto flex w-full max-w-3xl flex-col gap-6 py-4 max-md:pb-24">
        <RouterView />
      </main>
    </div>
    <Toaster />
  </TooltipProvider>
</template>
