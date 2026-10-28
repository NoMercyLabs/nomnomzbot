<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Edit the saved OBS link in a dialog. The password is write only: the box starts empty, a stored one is
// kept when the box stays empty, and "Remove password" clears it on purpose.
import { ref, watch } from 'vue';

import type { ObsConnection } from '@/api/obs';
import { useObsSaveConnection } from '@/api/obsControl';
import DateTime from '@/components/layout/DateTime.vue';
import EventKindMask from '@/components/layout/EventKindMask.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import { Button } from '@/components/ui/button';
import { FieldInfo } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { toastObsError } from '../obsToast';

const props = defineProps<{ connection: ObsConnection | null | undefined }>();

const open = defineModel<boolean>('open', { required: true });

const { channelId } = useCurrentChannel();
const save = useObsSaveConnection(channelId);

// The form shows only once the API has sent the connection (a channel with none stored reads back the
// server defaults), so these start empty and are filled from it, never from a default written here.
const host = ref<string>('');
const port = ref<string>('');
const mask = ref<number>(0);
const enabled = ref<boolean>(false);
const password = ref<string>('');
const clearPassword = ref<boolean>(false);

// Each opening starts from the stored link, so a canceled edit leaves nothing behind.
watch(
  [() => props.connection, open],
  ([connection, shown]) => {
    if (!connection || !shown) return;

    host.value = connection.host ?? '';
    port.value = String(connection.port ?? '');
    mask.value = Number(connection.eventSubscriptionsMask);
    enabled.value = connection.isEnabled;
    password.value = '';
    clearPassword.value = false;
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
  if (!props.connection) return;

  save.mutate({
    mode: props.connection.mode,
    host: host.value,
    port: Number.parseInt(port.value, 10),
    password: passwordToSend(),
    eventSubscriptionsMask: mask.value,
    isEnabled: enabled.value,
  }, {
    onSuccess: () => {
      open.value = false;
    },
    onError: toastObsError,
  });
}
</script>

<template>
  <FormDialog
    v-if="connection"
    v-model:open="open"
    data-obs-connection-dialog
    :title="$t('dashboard.obs.connectionForm.title')"
    :description="$t('dashboard.obs.connectionForm.description')"
    :action="$t('dashboard.obs.connectionForm.save')"
    :pending="save.isPending.value"
    @submit="submit"
  >
    <template #header-action>
      <LabeledSwitch
        v-model="enabled"
        data-obs-enabled
        :label="$t('dashboard.obs.connectionForm.enabled')"
      />
    </template>
    <div class="grid grid-cols-[1fr_6rem] gap-2">
      <div class="grid gap-1.5">
        <Label for="obs-host">{{ $t('dashboard.obs.connectionForm.host') }}</Label>
        <Input id="obs-host" v-model="host" data-obs-host />
      </div>
      <div class="grid gap-1.5">
        <Label for="obs-port">{{ $t('dashboard.obs.connectionForm.port') }}</Label>
        <Input id="obs-port"
               v-model="port"
               data-obs-port
               type="number"
               min="1"
               max="65535"
        />
      </div>
    </div>
    <div class="grid gap-1.5">
      <Label for="obs-password">{{ $t('dashboard.obs.connectionForm.password') }}</Label>
      <Input
        id="obs-password"
        v-model="password"
        data-obs-password
        type="password"
        autocomplete="new-password"
      />
      <p v-if="connection.hasPassword" data-obs-password-kept class="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
        {{ $t('dashboard.obs.connectionForm.passwordKept') }}
        <Button
          data-obs-password-clear
          type="button"
          variant="ghost"
          size="sm"
          :aria-pressed="clearPassword"
          @click="clearPassword = !clearPassword"
        >
          {{ $t('dashboard.obs.connectionForm.passwordClear') }}
        </Button>
      </p>
    </div>
    <fieldset class="grid gap-2">
      <legend class="flex items-center gap-1 text-sm font-medium">
        {{ $t('dashboard.obs.connectionForm.mask') }}
        <FieldInfo>{{ $t('dashboard.obs.connectionForm.maskHelp') }}</FieldInfo>
      </legend>
      <EventKindMask
        v-model="mask"
        data-obs-mask
        :kinds="connection.eventKinds"
        prefix="dashboard.obs.kinds"
      />
    </fieldset>
    <p v-if="connection.lastError" data-obs-last-error class="text-sm text-destructive">
      {{ $t('dashboard.obs.connectionForm.lastError', { error: connection.lastError }) }}
    </p>
    <p v-if="connection.lastConnectedAt" data-obs-last-connected class="text-sm text-muted-foreground">
      {{ $t('dashboard.obs.connectionForm.lastConnected') }}
      <DateTime :value="connection.lastConnectedAt" variant="relative" />
    </p>
  </FormDialog>
</template>
