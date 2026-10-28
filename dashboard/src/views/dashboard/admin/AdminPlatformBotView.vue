<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// The shared platform bot: its real connection state, and the reconnect that replaces it. The operator gives a
// reason, sees how many channels speak through the bot, confirms that count, then approves the new account
// with the device code. The state follows the admin hub.
import { useIntervalFn } from '@vueuse/core';
import { computed, ref } from 'vue';

import {
  usePreviewPlatformBotReconnect,
  usePollPlatformBotReconnect,
  useAdminPlatformBot,
  useStartPlatformBotReconnect,
} from '@/api/adminPlatformBot';
import { isBotLoginWaiting } from '@/api/whiteLabelBot';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';

const slowDownMs = 5000;

const { can } = usePlatformCan();

const status = useAdminPlatformBot();
const preview = usePreviewPlatformBotReconnect();
const start = useStartPlatformBotReconnect();
const poll = usePollPlatformBotReconnect();

const justification = ref('');
const previewed = ref<number | null>(null);
const outcome = ref<string | null>(null);
const intervalMs = ref(5000);

const statusState = usePanelState(status, (row) =>
  row === null);
const code = computed(() => start.data.value ?? null);
const reason = computed(() => justification.value.trim());
const connection = computed(() => status.data.value?.state ?? 'NeverConnected');
const failureKey = computed(() => {
  const message = (start.error.value ?? poll.error.value)?.message ?? '';

  return message.includes('PREVIEW_STALE')
    ? 'dashboard.admin.platformBot.stale'
    : 'dashboard.admin.platformBot.failed';
});
const failed = computed(
  () =>
    preview.isError.value ||
    start.isError.value ||
    poll.isError.value ||
    outcome.value !== null,
);

const timer = useIntervalFn(checkApproval, intervalMs, { immediate: false });

async function showPreview(): Promise<void> {
  outcome.value = null;
  previewed.value = await preview.mutateAsync(reason.value).catch(() => null);
}

async function begin(): Promise<void> {
  if (previewed.value === null) return;

  outcome.value = null;
  timer.pause();

  const started = await start
    .mutateAsync({ justification: reason.value, confirmedAffectedChannelCount: previewed.value })
    .catch(() => null);

  if (started === null) return;

  intervalMs.value = Number(started.interval) * 1000;
  timer.resume();
}

async function checkApproval(): Promise<void> {
  const deviceCode = code.value?.deviceCode;

  if (deviceCode === undefined || previewed.value === null || poll.isPending.value) return;

  const result = await poll
    .mutateAsync({
      deviceCode,
      justification: reason.value,
      confirmedAffectedChannelCount: previewed.value,
    })
    .catch(() => null);
  const answer = result?.status ?? 'error';

  if (answer === 'slow_down') {
    intervalMs.value += slowDownMs;
  }

  if (isBotLoginWaiting(answer)) return;

  timer.pause();
  start.reset();
  previewed.value = null;

  if (answer !== 'authorized') {
    outcome.value = answer;
  }
}
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.platformBot.title')" :crumb="$t('dashboard.admin.crumb')" />
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <PanelCard
      tier="lead"
      :title="$t('dashboard.admin.platformBot.state.title')"
      :description="$t('dashboard.admin.platformBot.state.description')"
      :state="statusState"
      :empty-text="$t('dashboard.admin.platformBot.state.empty')"
      :error-text="$t('dashboard.admin.platformBot.error')"
      @retry="status.refetch()"
    >
      <div class="flex items-center gap-3" data-bot-state>
        <Badge :variant="connection === 'ConnectedAndWorking' ? 'default' : 'outline'">
          {{ $t(`dashboard.admin.platformBot.state.${connection}`) }}
        </Badge>
        <span v-if="status.data.value?.botUsername" class="text-sm font-medium" data-bot-username>
          {{ status.data.value.botUsername }}
        </span>
      </div>
    </PanelCard>

    <PanelCard
      v-if="can('platform:bot:manage')"
      :title="$t('dashboard.admin.platformBot.reconnect.title')"
      :description="$t('dashboard.admin.platformBot.reconnect.description')"
    >
      <div class="grid max-w-xl gap-3">
        <div class="grid gap-1.5">
          <Label for="platform-bot-reason">{{ $t('dashboard.admin.platformBot.reconnect.reason') }}</Label>
          <Input id="platform-bot-reason" v-model="justification" data-field-reason />
        </div>
        <p v-if="previewed !== null" class="text-sm" data-preview>
          {{ $t('dashboard.admin.platformBot.reconnect.preview', { count: previewed }) }}
        </p>
        <template v-if="code">
          <p class="text-sm">
            {{ $t('dashboard.admin.platformBot.reconnect.instruction') }}
          </p>
          <p
            class="rounded-md border border-border bg-muted py-3 text-center font-mono text-2xl font-semibold tracking-widest select-all"
            data-user-code
          >
            {{ code.userCode }}
          </p>
          <Button as="a"
                  size="sm"
                  variant="outline"
                  :href="code.verificationUri"
                  target="_blank"
                  rel="noopener"
          >
            {{ $t('dashboard.admin.platformBot.reconnect.open') }}
          </Button>
          <p class="text-sm text-muted-foreground" role="status">
            {{ $t('dashboard.admin.platformBot.reconnect.waiting') }}
          </p>
        </template>
        <p v-if="failed" class="text-sm text-destructive" role="alert" data-failure>
          {{ $t(outcome !== null ? 'dashboard.admin.platformBot.denied' : failureKey) }}
        </p>
      </div>
      <template #footer>
        <div class="flex w-full justify-end gap-2">
          <Button
            v-if="previewed === null"
            data-action-preview
            :disabled="reason === '' || preview.isPending.value"
            @click="showPreview"
          >
            {{ $t('dashboard.admin.platformBot.reconnect.previewAction') }}
          </Button>
          <Button
            v-else-if="!code"
            data-action-start
            :disabled="start.isPending.value"
            @click="begin"
          >
            {{ $t('dashboard.admin.platformBot.reconnect.start', { count: previewed }) }}
          </Button>
        </div>
      </template>
    </PanelCard>
  </div>
</template>
