<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Where a channel's tips and memberships come from: one list of every service with its state. The setup
// details (address, secret, account) live in the service's own dialog, so the list carries no form and no
// row shouts: Set up and Manage are outline buttons, and Save in the dialog is the one primary action.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  supporterSources,
  useDeleteSupporterConnection,
  useSupporterConnections,
  type SupporterConnection,
  type SupporterSourceKey,
} from '@/api/supporters';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

import SupporterSourceDialog from './SupporterSourceDialog.vue';

type SourceState = 'connected' | 'paused' | 'none';

const tones = {
  connected: 'ok',
  paused: 'off',
  none: 'off',
} as const satisfies Record<SourceState, StatusTone>;

const { t } = useI18n();
const time = useTime();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const connections = useSupporterConnections(channelId);

const editing = ref<SupporterSourceKey | null>(null);
const remove = useDeleteSupporterConnection(channelId);
const removing = ref<SupporterSourceKey | null>(null);
const removeOpen = computed<boolean>({
  get: () => removing.value !== null,
  set: (value: boolean) => {
    if (!value) {
      removing.value = null;
    }
  },
});

function confirmRemove(): void {
  if (removing.value === null) return;

  remove.mutate(removing.value, {
    onSettled: (): void => {
      removing.value = null;
    },
  });
}

const state = usePanelState(connections, () => false);
const canEdit = computed<boolean>(() => can('supporters:config:write'));

const rows = computed(() =>
  (Object.keys(supporterSources) as SupporterSourceKey[]).map((key) => ({
    key,
    stored: connections.data.value?.find((entry: SupporterConnection) => entry.sourceKey === key),
  })),
);

function stateOf(stored: SupporterConnection | undefined): SourceState {
  if (stored === undefined)
    return 'none';

  return stored.isEnabled
    ? 'connected'
    : 'paused';
}

function detailOf(key: SupporterSourceKey, stored: SupporterConnection | undefined): string {
  if (stored?.lastEventAt)
    return t('dashboard.supporters.sources.lastEvent', { time: time.dateTime(stored.lastEventAt) });

  return stored === undefined
    ? t(`dashboard.supporters.connections.mode.${supporterSources[key].mode}`)
    : t('dashboard.supporters.sources.noEvents');
}
</script>

<template>
  <PageHeader :title="t('dashboard.supporters.sources.title')" />
  <PageBody>
    <PanelCard
      tier="lead"
      :title="t('dashboard.supporters.sources.listTitle')"
      :description="t('dashboard.supporters.sources.listNote')"
      :state="state"
      :error-text="t('dashboard.supporters.sources.error')"
      @retry="connections.refetch()"
    >
      <RowGroup>
        <SummaryRow
          v-for="row in rows"
          :key="row.key"
          :data-source="row.key"
          :title="t(`dashboard.supporters.connections.sources.${row.key}`)"
          :detail="detailOf(row.key, row.stored)"
        >
          <template #detail-lead>
            <StatusBadge
              :tone="tones[stateOf(row.stored)]"
              :label="t(`dashboard.supporters.sources.state.${stateOf(row.stored)}`)"
            />
          </template>
          <template v-if="canEdit" #trailing>
            <Button
              variant="outline"
              size="sm"
              :data-source-action="row.key"
              @click="editing = row.key"
            >
              {{
                row.stored === undefined
                  ? t("dashboard.supporters.sources.setUp")
                  : t("dashboard.supporters.sources.manage")
              }}
            </Button>
            <Button
              v-if="row.stored !== undefined"
              variant="destructive"
              size="sm"
              :data-source-remove="row.key"
              @click="removing = row.key"
            >
              {{ t("dashboard.supporters.sources.remove") }}
            </Button>
          </template>
        </SummaryRow>
      </RowGroup>
    </PanelCard>
    <SupporterSourceDialog v-if="canEdit" v-model:source="editing" />
    <ConfirmDialog
      v-model:open="removeOpen"
      :title="t('dashboard.supporters.sources.removeTitle', { name: removing ? t(`dashboard.supporters.connections.sources.${removing}`) : '' })"
      :description="t('dashboard.supporters.sources.removeDescription')"
      :action="t('dashboard.supporters.sources.removeAction')"
      :pending="remove.isPending.value"
      @confirm="confirmRemove"
    />
  </PageBody>
</template>
