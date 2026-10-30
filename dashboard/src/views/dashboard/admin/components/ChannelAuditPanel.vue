<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Audit tab of a channel console: the platform audit log narrowed to this channel, newest first, paged.
// The Audit page keeps the full filter set; here the channel is the only filter.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useAdminAudit, type AdminAuditEntry, type AdminAuditParams } from '@/api/adminAudit';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';

import AdminPager from './AdminPager.vue';
import AuditTable from './AuditTable.vue';

const PAGE_SIZE: number = 25;

const props = defineProps<{ channelId: string }>();

const { t } = useI18n();

const page = ref(1);

const params = computed(() => ({
  page: page.value,
  take: PAGE_SIZE,
  permission: '',
  outcome: '',
  principalId: '',
  targetBroadcasterId: props.channelId,
  from: '',
  to: '',
}) as const satisfies AdminAuditParams);

const log = useAdminAudit(params);

const rows = computed<AdminAuditEntry[]>(() => log.data.value?.data ?? []);
const total = computed(() => Number(log.data.value?.total ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

const state = usePanelState(log, (data) =>
  (data.data?.length ?? 0) === 0);
</script>

<template>
  <PanelCard
    :title="t('dashboard.admin.console.audit.title')"
    :description="t('dashboard.admin.console.audit.description')"
    :state="state"
    :empty-text="t('dashboard.admin.console.audit.empty')"
    :error-text="t('dashboard.admin.console.audit.error')"
    @retry="log.refetch()"
  >
    <AuditTable :rows="rows" />
    <template #footer>
      <AdminPager :page="page" :pages="pages" @previous="page -= 1" @next="page += 1" />
    </template>
  </PanelCard>
</template>
