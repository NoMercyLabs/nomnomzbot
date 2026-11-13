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

import SettingsPageFrame from './components/SettingsPageFrame.vue';
import ReadabilityView from './ReadabilityView.vue';

type SettingsPageKey = 'readability' | 'basics' | 'personality' | 'engagement';

const props = defineProps<{ view: SettingsPageKey }>();

// The page title is the sidebar entry's own word; each route names it in its meta.titleKey.
const pages = {
  readability: ReadabilityView,
  basics: defineAsyncComponent(() => import('./BasicsView.vue')),
  personality: defineAsyncComponent(() => import('./PersonalityView.vue')),
  engagement: defineAsyncComponent(() => import('./EngagementView.vue')),
} as const;

const page = computed(() => pages[props.view]);
</script>

<template>
  <SettingsPageFrame>
    <component :is="page" />
  </SettingsPageFrame>
</template>
