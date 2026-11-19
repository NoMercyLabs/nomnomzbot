<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The session card. With no session it offers Start; with one it offers End, after a confirm, because ending
// sends every guest away.
import { computed, ref } from 'vue';

import {
  useEndGuestStarSession,
  useStartGuestStarSession,
  type GuestStarSession,
} from '@/api/guestStar';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import type { PanelState } from '@/composables/usePanelState';

import { useGuestStarFailure } from '../guestStarFailure';

const props = defineProps<{
  session: GuestStarSession | null;
  state: PanelState;
}>();

defineEmits<{ retry: [] }>();

const { can } = useCan();
const { notify } = useGuestStarFailure();
const { channelId } = useCurrentChannel();

const start = useStartGuestStarSession(channelId);
const end = useEndGuestStarSession(channelId);
const canWrite = computed(() => can('guest-star:write'));
const confirming = ref(false);

function endSession(): void {
  if (props.session === null) return;

  end.mutate(props.session.id, {
    onSuccess: () => {
      confirming.value = false;
    },
    onError: (error) => {
      confirming.value = false;
      notify(error);
    },
  });
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.settings.guestStar.session.title')"
    :state="state"
    :error-text="$t('dashboard.settings.guestStar.loadError')"
    data-panel="session"
    @retry="$emit('retry')"
  >
    <RowGroup>
      <SummaryRow
        :title="session === null
          ? $t('dashboard.settings.guestStar.session.empty')
          : $t('dashboard.settings.guestStar.session.running')"
        :detail="session === null
          ? undefined
          : $t('dashboard.settings.guestStar.session.guestCount', { count: session.guests.length })"
      >
        <template v-if="canWrite" #trailing>
          <Button
            v-if="session === null"
            data-session-start
            :disabled="start.isPending.value"
            @click="start.mutate(undefined, { onError: notify })"
          >
            {{ $t("dashboard.settings.guestStar.session.start") }}
          </Button>
          <Button v-else variant="outline" data-session-end @click="confirming = true">
            {{ $t("dashboard.settings.guestStar.session.end") }}
          </Button>
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
  <ConfirmDialog
    v-model:open="confirming"
    :title="$t('dashboard.settings.guestStar.session.endTitle')"
    :description="$t('dashboard.settings.guestStar.session.endDescription')"
    :action="$t('dashboard.settings.guestStar.session.endAction')"
    :pending="end.isPending.value"
    @confirm="endSession"
  />
</template>
