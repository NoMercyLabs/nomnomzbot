<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The pending unban requests, read live from Twitch. Deny is quiet and instant. Approve lifts the ban, which the
// dashboard cannot take back, so it asks once more in the row before it acts.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useResolveUnbanRequest, useUnbanRequests, type UnbanRequest } from '@/api/unbanRequests';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

import SettingsPageFrame from '../components/SettingsPageFrame.vue';

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const time = useTime();

const requests = useUnbanRequests(channelId);
const resolve = useResolveUnbanRequest(channelId);
const state = usePanelState(requests, (list: UnbanRequest[]) =>
  list.length === 0);

// The one row that waits for the second press of Approve.
const confirming = ref<string | null>(null);

function detailOf(request: UnbanRequest): string {
  return `${request.text} · ${time.relative(request.createdAt)}`;
}

function settle(request: UnbanRequest, approve: boolean): void {
  confirming.value = null;

  resolve.mutate({ id: request.id, approve }, {
    onError: () => toast.error(t('dashboard.settings.unbanRequests.error', { name: request.userName })),
  });
}
</script>

<template>
  <SettingsPageFrame :crumb="$t('dashboard.settings.unbanRequests.crumb')">
    <p class="text-sm text-muted-foreground">{{ $t("dashboard.settings.unbanRequests.explain") }}</p>
    <PanelCard
      tier="lead"
      :title="$t('dashboard.settings.unbanRequests.listTitle')"
      :state="state"
      :empty-text="$t('dashboard.settings.unbanRequests.empty')"
      :error-text="$t('dashboard.settings.unbanRequests.loadError')"
      @retry="requests.refetch()"
    >
      <RowGroup>
        <SummaryRow
          v-for="request in requests.data.value"
          :key="request.id"
          :data-request="request.id"
          :title="request.userName"
          :detail="detailOf(request)"
        >
          <template v-if="can('moderation:unbanrequest:resolve')" #trailing>
            <Button
              v-if="confirming === request.id"
              variant="outline"
              size="sm"
              :data-request-confirm="request.id"
              :disabled="resolve.isPending.value"
              @click="settle(request, true)"
            >
              {{ $t("dashboard.settings.unbanRequests.confirm") }}
            </Button>
            <Button
              v-else
              variant="outline"
              size="sm"
              :data-request-approve="request.id"
              :disabled="resolve.isPending.value"
              @click="confirming = request.id"
            >
              {{ $t("dashboard.settings.unbanRequests.approve") }}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              :data-request-deny="request.id"
              :disabled="resolve.isPending.value"
              @click="settle(request, false)"
            >
              {{ $t("dashboard.settings.unbanRequests.deny") }}
            </Button>
          </template>
        </SummaryRow>
      </RowGroup>
    </PanelCard>
  </SettingsPageFrame>
</template>
