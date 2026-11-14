<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Event journal: what happened in the channel, in plain words. Saving or loading the journal and rebuilding the
// pages built from it are upkeep, so they sit on their own tab.
import { useI18n } from 'vue-i18n';

import PageTabs from '@/components/layout/PageTabs.vue';
import { useTabbedPage, type TabSpec } from '@/composables/useTabbedPage';

import SettingsPageFrame from '../components/SettingsPageFrame.vue';

import JournalFilePanel from './components/JournalFilePanel.vue';
import JournalListPanel from './components/JournalListPanel.vue';
import LegacyHistoryPanel from './components/LegacyHistoryPanel.vue';
import ReadModelsPanel from './components/ReadModelsPanel.vue';

const VIEWS = ['events', 'tools'] as const;

type JournalView = (typeof VIEWS)[number];

const { t } = useI18n();
const specs = () => [
  { value: 'events', label: t('dashboard.settings.eventJournal.tabs.events') },
  { value: 'tools', label: t('dashboard.settings.eventJournal.tabs.tools') },
] as const satisfies readonly TabSpec<JournalView>[];
const { tabs, view } = useTabbedPage(VIEWS, specs);
</script>

<template>
  <SettingsPageFrame>
    <template #tabs>
      <PageTabs :tabs="tabs" />
    </template>

    <JournalListPanel v-if="view === 'events'" />
    <template v-else>
      <JournalFilePanel />
      <ReadModelsPanel />
      <LegacyHistoryPanel />
    </template>
  </SettingsPageFrame>
</template>
