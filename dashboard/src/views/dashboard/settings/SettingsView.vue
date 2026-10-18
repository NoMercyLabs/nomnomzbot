<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
