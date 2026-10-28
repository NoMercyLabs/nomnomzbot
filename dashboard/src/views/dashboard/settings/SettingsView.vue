<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One settings page. The sidebar entry picks which one (the route passes `view`); the page is its own screen,
// with no menu inside it.
import { computed, defineAsyncComponent } from 'vue';

import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';

import ReadabilityView from './ReadabilityView.vue';

type SettingsPageKey = 'readability' | 'basics' | 'personality' | 'engagement';

const props = defineProps<{ view: SettingsPageKey }>();

// The page title is the sidebar entry's own word.
const titleKeys = {
  readability: 'frame.nav.settingsPages.readability',
  basics: 'frame.nav.settingsPages.botBasics',
  personality: 'frame.nav.settingsPages.botPersonality',
  engagement: 'frame.nav.settingsPages.engagementTriggers',
} as const satisfies Record<SettingsPageKey, string>;

const pages = {
  readability: ReadabilityView,
  basics: defineAsyncComponent(() => import('./BasicsView.vue')),
  personality: defineAsyncComponent(() => import('./PersonalityView.vue')),
  engagement: defineAsyncComponent(() => import('./EngagementView.vue')),
} as const;

const page = computed(() => pages[props.view]);
</script>

<template>
  <PageHeader :title="$t(titleKeys[view])" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <component :is="page" />
  </PageBody>
</template>
