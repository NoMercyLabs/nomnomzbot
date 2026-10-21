<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform's defaults: what a channel follows until it sets its own value. One section per kind of
// default, each its own PanelCard component (a new kind is one component added below). Every change shows
// the counted blast radius first; the queries share the ['admin', 'platform-defaults'] prefix.
import type { Component } from 'vue';

import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import { Button } from '@/components/ui/button';

import PlatformActionDefaults from './components/PlatformActionDefaults.vue';
import PlatformBuiltinReplyDefaults from './components/PlatformBuiltinReplyDefaults.vue';
import PlatformEventResponseDefaults from './components/PlatformEventResponseDefaults.vue';
import PlatformTtsVoiceDefault from './components/PlatformTtsVoiceDefault.vue';

// The sections in page order; on a phone the sticky menu below scrolls to each one.
const sections: { key: string; component: Component }[] = [
  { key: 'actions', component: PlatformActionDefaults },
  { key: 'replies', component: PlatformBuiltinReplyDefaults },
  { key: 'events', component: PlatformEventResponseDefaults },
  { key: 'voice', component: PlatformTtsVoiceDefault },
];

function jumpTo(key: string): void {
  document.querySelector(`[data-section="${key}"]`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
}
</script>

<template>
  <PageHeader
    :title="$t('dashboard.admin.platformDefaults.title')"
    :crumb="$t('dashboard.admin.platformDefaults.crumb')"
  />
  <PageBody>
    <p class="text-sm text-muted-foreground">
      {{ $t("dashboard.admin.platformDefaults.explain") }}
    </p>
    <nav
      data-jump-menu
      :aria-label="$t('dashboard.admin.spamDefaults.jumpTo')"
      class="sticky top-0 z-10 -mx-4 flex gap-2 overflow-x-auto scrollbar-none bg-background px-4 py-2 md:hidden"
    >
      <Button
        v-for="section in sections"
        :key="section.key"
        type="button"
        variant="outline"
        class="shrink-0"
        :data-jump="section.key"
        @click="jumpTo(section.key)"
      >
        {{ $t(`dashboard.admin.platformDefaults.${section.key}.title`) }}
      </Button>
    </nav>
    <section
      v-for="section in sections"
      :key="section.key"
      :data-section="section.key"
      class="scroll-mt-14"
    >
      <component :is="section.component" />
    </section>
  </PageBody>
</template>
