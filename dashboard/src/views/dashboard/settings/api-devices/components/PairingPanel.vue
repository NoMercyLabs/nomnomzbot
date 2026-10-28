<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Two ways to pair a device. The device asks you: type the code it shows into "Approve a device".
// You ask the device: name it, make a code, and type that code into the device.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useApproveDevice,
  useMintPairingCode,
  type PairingCode,
} from '@/api/automationTokens';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useTime } from '@/composables/useTime';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const mint = useMintPairingCode();
const approve = useApproveDevice(channelId);

const label = ref('');
const code = ref<PairingCode | undefined>(undefined);
const userCode = ref('');
const approved = ref(false);
const error = ref<string | undefined>(undefined);

async function makeCode(): Promise<void> {
  error.value = undefined;

  try {
    code.value = await mint.mutateAsync({ deviceLabel: label.value.trim(), scopes: [] });
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

async function approveCode(): Promise<void> {
  error.value = undefined;
  approved.value = false;

  try {
    await approve.mutateAsync(userCode.value.trim());
    approved.value = true;
    userCode.value = '';
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}
</script>

<template>
  <PanelCard
    data-panel="pairing"
    pad
    :title="$t('dashboard.settings.apiDevices.pairing.title')"
    :description="$t('dashboard.settings.apiDevices.pairing.description')"
  >
    <div class="grid gap-6 md:grid-cols-2">
      <form class="grid content-start gap-2" @submit.prevent="makeCode">
        <h3 class="text-sm font-semibold">{{ $t('dashboard.settings.apiDevices.pairing.mint.title') }}</h3>
        <Input
          v-model="label"
          data-field="device-label"
          maxlength="80"
          :placeholder="$t('dashboard.settings.apiDevices.pairing.mint.placeholder')"
        />
        <Button
          data-mint
          type="submit"
          variant="outline"
          :disabled="label.trim() === '' || mint.isPending.value"
        >
          {{ $t('dashboard.settings.apiDevices.pairing.mint.action') }}
        </Button>
        <p v-if="code" data-pair-code class="text-sm">
          <span class="font-mono text-lg tracking-widest">{{ code.code }}</span>
          <span class="block text-muted-foreground">
            {{ $t('dashboard.settings.apiDevices.pairing.mint.expires', { time: time.timeSeconds(code.expiresAt) }) }}
          </span>
        </p>
      </form>
      <form class="grid content-start gap-2" @submit.prevent="approveCode">
        <h3 class="text-sm font-semibold">{{ $t('dashboard.settings.apiDevices.pairing.approve.title') }}</h3>
        <Input
          v-model="userCode"
          data-field="user-code"
          maxlength="16"
          :placeholder="$t('dashboard.settings.apiDevices.pairing.approve.placeholder')"
        />
        <Button
          data-approve
          type="submit"
          variant="outline"
          :disabled="userCode.trim() === '' || approve.isPending.value"
        >
          {{ $t('dashboard.settings.apiDevices.pairing.approve.action') }}
        </Button>
        <p v-if="approved" data-approved class="text-sm text-muted-foreground">
          {{ $t('dashboard.settings.apiDevices.pairing.approve.done') }}
        </p>
      </form>
    </div>
    <p v-if="error" role="alert" class="mt-4 text-sm text-destructive">{{ error }}</p>
  </PanelCard>
</template>
