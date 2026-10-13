<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The four "what you get" blocks: copy on one side, a small sample on the other, alternating.
import { useI18n } from 'vue-i18n'

import DemoRows from './DemoRows.vue'
import PlatformTabs from './PlatformTabs.vue'

const blocks: string[] = ['chat', 'moderation', 'alerts', 'platforms']
const { t } = useI18n()
</script>

<template>
  <!-- 64 between blocks against 32 inside one, so each copy reads with its own sample. -->
  <section id="outcomes" class="flex scroll-mt-6 flex-col gap-16">
    <article
      v-for="(block, index) in blocks"
      :key="block"
      class="grid items-center gap-8 md:grid-cols-2"
    >
      <!-- min-w-0: a grid column grows to its widest child by default, which pushed the page past a phone screen. -->
      <div class="min-w-0" :class="index % 2 === 1 ? 'md:order-2' : ''">
        <h2 class="text-2xl font-semibold">{{ t(`site.home.outcomes.${block}.title`) }}</h2>
        <p class="mt-2 text-base text-muted-foreground">
          {{ t(`site.home.outcomes.${block}.body`) }}
        </p>
      </div>
      <!-- Panel radius 20, 8px padding, so the group inside is 20 - 8 = 12. -->
      <div class="min-w-0 rounded-lg border border-border bg-card p-2">
        <PlatformTabs v-if="block === 'platforms'" />
        <DemoRows v-else-if="block === 'chat'" kind="commands" />
        <DemoRows v-else-if="block === 'moderation'" kind="queue" />
        <DemoRows v-else kind="sub" />
      </div>
    </article>
  </section>
</template>
