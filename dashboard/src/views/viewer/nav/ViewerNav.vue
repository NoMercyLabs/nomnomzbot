<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The viewer's navigation for one channel: the seven pages under /me/<channel>. A row of links under the header
// on a wide screen, a bar along the bottom edge on a narrow one (one element, two places, no second list).
import { useMediaQuery } from '@vueuse/core';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';

import { Button } from '@/components/ui/button';

import { viewerNavEntries } from './viewerNavigation';

const { t } = useI18n();
const route = useRoute();

// The phone bar shows the one-word labels; the row from the tablet width up has room for the full ones.
const phone = useMediaQuery('(max-width: 767px)');
const channel = computed(() => String(route.params.channel ?? ''));
</script>

<template>
  <nav
    v-if="channel !== ''"
    data-slot="viewer-nav"
    :aria-label="t('frame.nav.label')"
    class="fixed inset-x-0 bottom-0 z-10 flex items-stretch justify-around gap-1 border-t border-border bg-background p-1 md:static md:justify-start md:border-0 md:bg-transparent md:p-0"
  >
    <Button
      v-for="entry in viewerNavEntries"
      :key="entry.route"
      :variant="route.name === entry.route ? 'secondary' : 'ghost'"
      class="flex-1 h-auto! min-h-11 min-w-0 flex-col gap-1! px-1! py-1.5 text-xs md:h-9! md:min-h-0 md:flex-none md:flex-row md:gap-2! md:px-3! md:py-0 md:text-sm"
      as-child
    >
      <RouterLink
        :to="{ name: entry.route, params: { channel } }"
        class="no-underline!"
        :aria-current="route.name === entry.route ? 'page' : undefined"
      >
        <component :is="entry.icon" class="size-5 md:size-4" aria-hidden="true" />
        <span class="min-w-0 max-w-full text-center leading-tight">{{ t(`frame.nav.${phone ? entry.shortLabel : entry.label}`) }}</span>
      </RouterLink>
    </Button>
  </nav>
</template>
