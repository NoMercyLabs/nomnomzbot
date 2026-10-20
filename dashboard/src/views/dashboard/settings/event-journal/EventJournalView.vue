<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Event journal: what happened in the channel, in plain words. Saving or loading the journal and rebuilding the
// pages built from it are upkeep, so they sit on their own tab.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import { usePageView } from '@/composables/usePageView';

import JournalFilePanel from './components/JournalFilePanel.vue';
import JournalListPanel from './components/JournalListPanel.vue';
import LegacyHistoryPanel from './components/LegacyHistoryPanel.vue';
import ReadModelsPanel from './components/ReadModelsPanel.vue';

const VIEWS = ['events', 'tools'] as const;

type JournalView = (typeof VIEWS)[number];

const { t } = useI18n();
const view = usePageView(VIEWS);
const tabs = computed<PageTab<JournalView>[]>(() => [
  { value: 'events', label: t('dashboard.settings.eventJournal.tabs.events') },
  { value: 'tools', label: t('dashboard.settings.eventJournal.tabs.tools') },
]);
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.eventJournal')" :crumb="$t('dashboard.settings.crumb')">
    <template #bar>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>

  <PageBody>
    <JournalListPanel v-if="view === 'events'" />
    <template v-else>
      <JournalFilePanel />
      <ReadModelsPanel />
      <LegacyHistoryPanel />
    </template>
  </PageBody>
</template>
