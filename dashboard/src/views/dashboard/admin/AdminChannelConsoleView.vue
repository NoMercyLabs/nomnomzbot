<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One channel's console (/app/admin/channels/:channelId/:tab?): the header, then one tab per source the bot
// has today. The tab is in the address, so a reload or a copied link opens the same tab; an unknown tab
// opens Summary. Later admin tabs mount here as one more entry of TABS.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useAdminTenant } from '@/api/admin';
import { useChannelDiagnostics } from '@/api/adminDiagnostics';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { usePanelState } from '@/composables/usePanelState';

import BillingChannelPanel from './components/BillingChannelPanel.vue';
import ChannelAuditPanel from './components/ChannelAuditPanel.vue';
import ChannelConsoleHeader from './components/ChannelConsoleHeader.vue';
import ChannelRunsPanel from './components/ChannelRunsPanel.vue';
import ChannelSummaryPanel from './components/ChannelSummaryPanel.vue';
import ChannelTtsPanel from './components/ChannelTtsPanel.vue';
import { withoutRunsKeys } from './components/runsQuery';
import TenantActions from './components/TenantActions.vue';
import TenantLimitsPanel from './components/TenantLimitsPanel.vue';
import TenantMembersPanel from './components/TenantMembersPanel.vue';

const TABS = [
  'summary',
  'runs',
  'tts',
  'billing',
  'people',
  'limits',
  'audit',
  'data',
] as const;

const { t } = useI18n();
const route = useRoute();
const router = useRouter();

const channelId = computed<string>(() => String(route.params.channelId));
const activeTab = computed<(typeof TABS)[number]>(() =>
  TABS.find((known) => known === route.params.tab) ?? 'summary');

const detail = useAdminTenant(channelId);
const tenant = computed(() => detail.data.value ?? null);
const detailState = usePanelState(detail, (data) =>
  data === null);

const diagnostics = useChannelDiagnostics(channelId, ['summary']);
const summary = computed(() => diagnostics.data.value?.summary ?? null);

function openTab(tab: string | number): void {
  void router.push({
    name: 'dashboard-admin-channels-console',
    params: { channelId: channelId.value, tab: String(tab) },
    query: withoutRunsKeys(route.query),
  });
}
</script>

<template>
  <PageHeader
    :title="tenant?.name ?? t('dashboard.admin.console.title')"
    :crumb="t('dashboard.admin.title')"
    :back="{ name: 'dashboard-admin-channels', query: route.query }"
    :back-label="t('dashboard.admin.console.back')"
  />
  <PageBody data-page-body>
    <template v-if="tenant">
      <ChannelConsoleHeader
        :channel="tenant"
        :live="summary?.isLive ?? false"
        :tier="summary?.tierDisplayName"
        :tab="activeTab"
      />
      <Tabs :key="channelId" :model-value="activeTab" class="min-w-0 gap-3" @update:model-value="openTab">
        <div class="max-w-full overflow-x-auto scrollbar-none">
          <TabsList :aria-label="t('dashboard.admin.console.tabsLabel')">
            <TabsTrigger v-for="tab in TABS" :key="tab" :value="tab">
              {{ t(`dashboard.admin.console.tabs.${tab}`) }}
            </TabsTrigger>
          </TabsList>
        </div>
        <TabsContent value="summary">
          <ChannelSummaryPanel :tenant="tenant" />
        </TabsContent>
        <TabsContent value="runs">
          <ChannelRunsPanel :channel-id="channelId" />
        </TabsContent>
        <TabsContent value="tts">
          <ChannelTtsPanel :channel-id="channelId" />
        </TabsContent>
        <TabsContent value="billing">
          <BillingChannelPanel :channel="channelId" />
        </TabsContent>
        <TabsContent value="people">
          <TenantMembersPanel :tenant-id="channelId" />
        </TabsContent>
        <TabsContent value="limits">
          <TenantLimitsPanel :tenant-id="channelId" />
        </TabsContent>
        <TabsContent value="audit">
          <ChannelAuditPanel :channel-id="channelId" />
        </TabsContent>
        <TabsContent value="data">
          <PanelCard
            pad
            :title="t('dashboard.admin.console.data.title')"
            :description="t('dashboard.admin.console.data.description')"
          >
            <TenantActions :tenant="tenant" />
          </PanelCard>
        </TabsContent>
      </Tabs>
    </template>
    <PanelCard
      v-else
      :title="t('dashboard.admin.console.title')"
      :state="detailState"
      :empty-text="t('dashboard.admin.console.error')"
      :error-text="t('dashboard.admin.console.error')"
      @retry="detail.refetch()"
    />
  </PageBody>
</template>
