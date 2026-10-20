<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The device login of the channel's own bot: show the code, send the person to Twitch to approve it as the
// bot account, and poll until Twitch answers. The poll steps back when Twitch asks for it (slow_down).
import { useIntervalFn } from '@vueuse/core';
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { ApiError } from '@/api/errors';
import {
  isBotLoginWaiting,
  usePollBotLogin,
  useStartBotBrowserLogin,
  useStartBotLogin,
} from '@/api/whiteLabelBot';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const slowDownMs = 5000;
const outcomes = ['expired', 'denied', 'error'] as const;

const emit = defineEmits<{ cancel: []; connected: [] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const start = useStartBotLogin(channelId);
const poll = usePollBotLogin(channelId);
const browserLogin = useStartBotBrowserLogin(channelId);

const intervalMs = ref(5000);
const outcome = ref<string | null>(null);

const code = computed(() => start.data.value ?? null);
const site = computed(() =>
  code.value
    ? new URL(code.value.verificationUri).host
    : '');
const failed = computed(() => start.isError.value || outcome.value !== null);
const failureKey = computed(() =>
  outcome.value !== null && outcomes.some((item) => item === outcome.value)
    ? `dashboard.settings.whiteLabelBot.login.${outcome.value}`
    : 'dashboard.settings.whiteLabelBot.login.error');

const timer = useIntervalFn(checkApproval, intervalMs, { immediate: false });

async function checkApproval(): Promise<void> {
  const deviceCode = code.value?.deviceCode;

  if (deviceCode === undefined || poll.isPending.value) return;

  const result = await poll.mutateAsync(deviceCode).catch(() => null);
  const status = result?.status ?? 'error';

  if (status === 'slow_down') {
    intervalMs.value += slowDownMs;
  }

  if (isBotLoginWaiting(status)) return;

  timer.pause();

  if (status === 'authorized') {
    emit('connected');

    return;
  }

  outcome.value = status;
}

async function begin(): Promise<void> {
  outcome.value = null;
  timer.pause();

  const started = await start.mutateAsync().catch(() => null);

  if (started === null) return;

  intervalMs.value = Number(started.interval) * 1000;
  timer.resume();
}

function openBrowserLogin(): void {
  browserLogin.mutate(undefined, {
    onSuccess: (started) => {
      window.open(started.authorizeUrl, '_blank', 'noopener');
    },
    onError: (error: Error) =>
      toast.error(
        error instanceof ApiError
          ? t(error.messageKey)
          : t('dashboard.settings.whiteLabelBot.browserFailed'),
      ),
  });
}

onMounted(begin);
</script>

<template>
  <div class="flex flex-col gap-3 p-3" data-slot="bot-login">
    <template v-if="!failed">
      <p v-if="!code" class="text-sm text-muted-foreground" role="status">
        {{ $t('dashboard.settings.whiteLabelBot.login.starting') }}
      </p>
      <template v-else>
        <p class="text-sm">{{ $t('dashboard.settings.whiteLabelBot.login.instruction', { site }) }}</p>
        <p
          class="rounded-md border border-border bg-muted py-3 text-center font-mono text-2xl font-semibold tracking-widest select-all"
          :aria-label="$t('dashboard.settings.whiteLabelBot.login.codeLabel')"
        >
          {{ code.userCode }}
        </p>
        <p class="text-sm text-muted-foreground">
          {{ $t('dashboard.settings.whiteLabelBot.login.botAccountHint') }}
        </p>
        <div class="flex flex-wrap items-center gap-2">
          <Button as="a"
                  size="sm"
                  :href="code.verificationUri"
                  target="_blank"
                  rel="noopener"
          >
            {{ $t('dashboard.settings.whiteLabelBot.login.open', { site }) }}
          </Button>
          <Button variant="ghost" size="sm" @click="emit('cancel')">
            {{ $t('dashboard.settings.whiteLabelBot.login.cancel') }}
          </Button>
        </div>
        <p class="text-sm text-muted-foreground" role="status">
          {{ $t('dashboard.settings.whiteLabelBot.login.waiting') }}
        </p>
        <div class="flex flex-wrap items-center gap-x-3 gap-y-2 border-t border-border pt-3">
          <p class="min-w-0 flex-1 basis-56 text-sm text-muted-foreground">
            {{ $t('dashboard.settings.whiteLabelBot.browser.description') }}
          </p>
          <Button
            variant="outline"
            size="sm"
            :disabled="browserLogin.isPending.value"
            @click="openBrowserLogin"
          >
            {{ $t('dashboard.settings.whiteLabelBot.browser.action') }}
          </Button>
        </div>
      </template>
    </template>
    <template v-else>
      <p class="text-sm" role="alert">{{ $t(failureKey) }}</p>
      <div class="flex flex-wrap items-center gap-2">
        <Button size="sm" @click="begin">
          {{ $t('dashboard.settings.whiteLabelBot.login.again') }}
        </Button>
        <Button variant="ghost" size="sm" @click="emit('cancel')">
          {{ $t('dashboard.settings.whiteLabelBot.login.cancel') }}
        </Button>
      </div>
    </template>
  </div>
</template>
