<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Event journal: save or load the channel's record of events, and rebuild the pages that are built from it.
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';

import JournalFilePanel from './components/JournalFilePanel.vue';
import JournalListPanel from './components/JournalListPanel.vue';
import LegacyHistoryPanel from './components/LegacyHistoryPanel.vue';
import ReadModelsPanel from './components/ReadModelsPanel.vue';
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.eventJournal')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <JournalListPanel />
    <JournalFilePanel />
    <ReadModelsPanel />
    <LegacyHistoryPanel />
  </PageBody>
</template>
