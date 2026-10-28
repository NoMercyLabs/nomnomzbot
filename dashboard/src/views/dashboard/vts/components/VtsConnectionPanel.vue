<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The VTube Studio link: where the bot dials, whether the one-time approval is done, and the bridge credential.
// Save is the one primary action; Authorize and Rotate are outline, and rotating asks first.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useVtsAuthorize,
  useVtsRotateBridgeToken,
  useVtsSaveConnection,
  type VtsConnection,
} from '@/api/vts';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import DateTime from '@/components/layout/DateTime.vue';
import EventKindMask from '@/components/layout/EventKindMask.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import { Switch } from '@/components/ui/switch';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { toastVtsError } from '../vtsToast';
import { vtsHealth, type VtsHealth } from '../vtsHealth';

const props = defineProps<{
  connection: VtsConnection | null | undefined;
  panelState: PanelState;
}>();

defineEmits<{ retry: [] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const save = useVtsSaveConnection(channelId);
const authorize = useVtsAuthorize(channelId);
const rotate = useVtsRotateBridgeToken(channelId);

// The form shows only once the API has sent the connection (a channel with none stored reads back the
// server defaults), so these start empty and are filled from it, never from a default written here.
const mode = ref<string>('');
const endpoint = ref<string>('');
const mask = ref<number>(0);
const enabled = ref<boolean>(false);
const confirming = ref<boolean>(false);

watch(
  () => props.connection,
  (connection) => {
    if (!connection) return;

    mode.value = connection.mode;
    endpoint.value = connection.endpoint;
    mask.value = Number(connection.eventSubscriptionsMask);
    enabled.value = connection.isEnabled;
  },
  { immediate: true },
);

const health = computed<VtsHealth>(() => vtsHealth(props.connection));
const modes = computed(() => [
  { value: 'direct', label: t('dashboard.vts.connection.modes.direct') },
  { value: 'bridge', label: t('dashboard.vts.connection.modes.bridge') },
]);
const tokenText = computed<string>(() =>
  t(
    props.connection?.hasPluginToken
      ? 'dashboard.vts.connection.tokenStored'
      : 'dashboard.vts.connection.tokenMissing',
  ),
);

function submit(): void {
  save.mutate(
    {
      mode: mode.value,
      endpoint: endpoint.value,
      eventSubscriptionsMask: mask.value,
      isEnabled: enabled.value,
    },
    { onError: toastVtsError },
  );
}

function rotateNow(): void {
  rotate.mutate(undefined, {
    onSuccess: () => {
      confirming.value = false;
    },
    onError: toastVtsError,
  });
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.vts.connection.title')"
    :state="panelState"
    :error-text="$t('dashboard.vts.connection.loadError')"
    @retry="$emit('retry')"
  >
    <div data-vts-connection class="px-1 py-0.5">
      <SummaryRow
        :title="$t('dashboard.vts.connection.link')"
        :detail="$t(`dashboard.vts.status.${health.reason}`)"
      >
        <template #detail-lead>
          <span data-vts-dot class="dot" :class="health.dot" aria-hidden="true" />
        </template>
      </SummaryRow>
      <SummaryRow
        v-if="connection?.lastConnectedAt"
        data-vts-last-connected
        :title="$t('dashboard.vts.connection.lastConnected')"
      >
        <template #detail-lead>
          <DateTime :value="connection.lastConnectedAt" variant="relative" />
        </template>
      </SummaryRow>
      <SummaryRow :title="$t('dashboard.vts.connection.token')" :detail="tokenText">
        <template #detail-lead>
          <span
            data-vts-token-dot
            class="dot"
            :class="connection?.hasPluginToken ? 'ok' : 'warn'"
            aria-hidden="true"
          />
        </template>
      </SummaryRow>
    </div>
    <form v-if="connection" class="grid gap-3 p-3" @submit.prevent="submit">
      <fieldset :disabled="!can('vts:config:write')" class="grid gap-3">
        <label class="flex items-center justify-between gap-3 text-sm font-medium">
          {{ $t("dashboard.vts.connection.enabled") }}
          <Switch
            v-model="enabled"
            data-vts-enabled
            :aria-label="$t('dashboard.vts.connection.enabled')"
          />
        </label>
        <SegmentedControl
          v-model="mode"
          data-vts-mode
          :label="$t('dashboard.vts.connection.mode')"
          :options="modes"
        />
        <label class="grid gap-1 text-sm text-muted-foreground">
          {{ $t("dashboard.vts.connection.endpoint") }}
          <Input v-model="endpoint" data-vts-endpoint />
        </label>
        <div class="grid gap-2 text-sm text-muted-foreground">
          {{ $t("dashboard.vts.connection.mask") }}
          <EventKindMask
            v-model="mask"
            data-vts-mask
            :kinds="connection.eventKinds"
            prefix="dashboard.vts.connection.kinds"
            :disabled="!can('vts:config:write')"
          />
          <span>{{ $t("dashboard.vts.connection.maskHelp") }}</span>
        </div>
      </fieldset>
      <div v-if="can('vts:config:write')" class="flex flex-wrap justify-end gap-2">
        <Button
          v-if="mode === 'bridge'"
          data-vts-rotate
          type="button"
          variant="outline"
          @click="confirming = true"
        >
          {{ $t("dashboard.vts.connection.rotate") }}
        </Button>
        <Button
          data-vts-authorize
          type="button"
          variant="outline"
          :disabled="authorize.isPending.value"
          @click="authorize.mutate(undefined, { onError: toastVtsError })"
        >
          <Spinner v-if="authorize.isPending.value" />
          {{
            authorize.isPending.value
              ? $t("dashboard.vts.connection.waiting")
              : $t("dashboard.vts.connection.authorize")
          }}
        </Button>
        <Button data-vts-save type="submit" :disabled="save.isPending.value">
          {{ $t("dashboard.vts.connection.save") }}
        </Button>
      </div>
      <p v-if="authorize.isPending.value" class="text-sm text-muted-foreground">
        {{ $t("dashboard.vts.connection.waitingHelp") }}
      </p>
    </form>
    <ConfirmDialog
      v-model:open="confirming"
      :title="$t('dashboard.vts.connection.rotateConfirm.title')"
      :description="$t('dashboard.vts.connection.rotateConfirm.description')"
      :consequences="[$t('dashboard.vts.connection.rotateConfirm.old')]"
      :action="$t('dashboard.vts.connection.rotateConfirm.action')"
      :pending="rotate.isPending.value"
      @confirm="rotateNow"
    />
  </PanelCard>
</template>
