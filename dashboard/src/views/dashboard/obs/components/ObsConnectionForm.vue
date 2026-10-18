<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Edit the saved OBS link. The password is write only: the box starts empty, a stored one is
// kept when the box stays empty, and "Remove password" clears it on purpose.
import { ref, watch } from 'vue';

import type { ObsConnection } from '@/api/obs';
import { useObsSaveConnection } from '@/api/obsControl';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { toastObsError } from '../obsToast';

const props = defineProps<{ connection: ObsConnection | null | undefined }>();

const { channelId } = useCurrentChannel();
const save = useObsSaveConnection(channelId);

const host = ref<string>('');
const port = ref<string>('');
const mask = ref<string>('');
const enabled = ref<boolean>(false);
const password = ref<string>('');
const clearPassword = ref<boolean>(false);

watch(
  () => props.connection,
  (connection) => {
    host.value = connection?.host ?? '';
    port.value = String(connection?.port ?? '');
    mask.value = String(connection?.eventSubscriptionsMask ?? '');
    enabled.value = connection?.isEnabled === true;
  },
  { immediate: true },
);

// Null keeps the stored password, an empty string clears it, text replaces it.
function passwordToSend(): string | null {
  if (password.value !== '')
    return password.value;

  return clearPassword.value
    ? ''
    : null;
}

function submit(): void {
  save.mutate({
    mode: props.connection?.mode ?? 'direct',
    host: host.value,
    port: Number.parseInt(port.value, 10),
    password: passwordToSend(),
    eventSubscriptionsMask: Number.parseInt(mask.value, 10),
    isEnabled: enabled.value,
  }, {
    onSuccess: () => {
      password.value = '';
      clearPassword.value = false;
    },
    onError: toastObsError,
  });
}
</script>

<template>
  <form class="grid gap-3 p-3" @submit.prevent="submit">
    <label class="flex items-center justify-between gap-3 text-sm font-medium">
      {{ $t('dashboard.obs.connectionForm.enabled') }}
      <Switch
        v-model="enabled"
        data-obs-enabled
        :aria-label="$t('dashboard.obs.connectionForm.enabled')"
      />
    </label>
    <div class="grid grid-cols-[1fr_6rem] gap-2">
      <label class="grid gap-1 text-sm text-muted-foreground">
        {{ $t('dashboard.obs.connectionForm.host') }}
        <Input v-model="host" data-obs-host />
      </label>
      <label class="grid gap-1 text-sm text-muted-foreground">
        {{ $t('dashboard.obs.connectionForm.port') }}
        <Input v-model="port"
               data-obs-port
               type="number"
               min="1"
               max="65535"
        />
      </label>
    </div>
    <label class="grid gap-1 text-sm text-muted-foreground">
      {{ $t('dashboard.obs.connectionForm.password') }}
      <Input
        v-model="password"
        data-obs-password
        type="password"
        autocomplete="new-password"
        :placeholder="connection?.hasPassword ? $t('dashboard.obs.connectionForm.passwordKept') : ''"
      />
    </label>
    <p v-if="connection?.hasPassword" class="text-sm text-muted-foreground">
      {{ $t('dashboard.obs.connectionForm.passwordKept') }}
      <Button
        data-obs-password-clear
        type="button"
        variant="ghost"
        :aria-pressed="clearPassword"
        @click="clearPassword = !clearPassword"
      >
        {{ $t('dashboard.obs.connectionForm.passwordClear') }}
      </Button>
    </p>
    <label class="grid gap-1 text-sm text-muted-foreground">
      {{ $t('dashboard.obs.connectionForm.mask') }}
      <Input v-model="mask" data-obs-mask type="number" min="0" />
      <span>{{ $t('dashboard.obs.connectionForm.maskHelp') }}</span>
    </label>
    <div class="flex justify-end">
      <Button data-obs-save type="submit" :disabled="save.isPending.value">
        {{ $t('dashboard.obs.connectionForm.save') }}
      </Button>
    </div>
  </form>
</template>
