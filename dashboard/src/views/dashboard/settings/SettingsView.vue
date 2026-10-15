<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The settings page. Channel: a list of settings beside the open one (only Readability is built so far).
// This device: what stays in this browser, with a link back into the Channel list.
import ArrowLeft from '~icons/nnz/arrow-left';
import BookOpenText from '~icons/nnz/book-open';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import ListDetail from '@/components/layout/ListDetail.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { usePageView } from '@/composables/usePageView';

import DeviceView from './DeviceView.vue';
import ReadabilityView from './ReadabilityView.vue';

const { t } = useI18n();

const tabs = computed<PageTab[]>(() => [
  { value: 'channel', label: t('dashboard.settings.tabs.channel') },
  { value: 'device', label: t('dashboard.settings.tabs.device') },
]);
const view = usePageView(() => tabs.value.map((tab: PageTab) => tab.value));

// On a phone the section list is the first screen; a picked section opens over it.
const opened = ref(false);

function openReadability(): void {
  opened.value = true;
  view.value = 'channel';
}
</script>

<template>
  <PageHeader :title="$t('dashboard.settings.title')" :crumb="$t('dashboard.settings.crumb')">
    <template #bar><PageTabs :tabs="tabs" /></template>
  </PageHeader>

  <ListDetail v-if="view === 'channel'" :show-detail="opened">
    <template #list>
      <PanelCard :title="$t('dashboard.settings.nav.you')" tier="quiet">
        <RowGroup>
          <SummaryRow
            pick
            selected
            :title="$t('dashboard.settings.nav.readability')"
            @click="opened = true"
          >
            <template #leading><BookOpenText class="size-4 shrink-0" /></template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>
    </template>
    <template #detail>
      <div class="flex flex-1 flex-col gap-2">
        <Button variant="ghost" size="sm" class="self-start md:hidden" @click="opened = false">
          <ArrowLeft aria-hidden="true" />
          {{ $t('dashboard.settings.back') }}
        </Button>
        <ReadabilityView />
      </div>
    </template>
  </ListDetail>

  <PageBody v-else>
    <DeviceView @open-readability="openReadability" />
  </PageBody>
</template>
