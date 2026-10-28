<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Your data: what the bot holds about you and what you can do with it. The page has four parts: a download of
// everything the bot holds about you, the consents you gave, your requests so far, and the two ways to limit or
// end the use of your data. Erasing is irreversible, so it opens only after the bot counts what it would destroy.
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';

import ConsentsPanel from './components/ConsentsPanel.vue';
import ErasePanel from './components/ErasePanel.vue';
import ExportPanel from './components/ExportPanel.vue';
import RequestsPanel from './components/RequestsPanel.vue';
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.yourData')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <div data-view="your-data" class="flex flex-col gap-4">
      <ExportPanel />
      <ConsentsPanel />
      <RequestsPanel />
      <ErasePanel />
    </div>
  </PageBody>
</template>
