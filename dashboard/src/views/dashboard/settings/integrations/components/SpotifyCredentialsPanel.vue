<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Spotify app the channel connects through. The row names the app in use: the channel's own app (the last 4
// characters of its client id) or the shared app. The client id and secret are edited in a dialog, never in the
// page. The bot never sends the secret back: the page only learns that one is stored. Saving replaces both
// values; clearing falls back to the shared Spotify app only when the server reports one (hasSharedApp), and
// otherwise just removes the channel's own app. The dialog opens by itself only when Spotify is not connected,
// no shared app exists and no own app is stored: then nothing else can make Spotify work.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  useClearSpotifyCredentials,
  useIntegrations,
  useSaveSpotifyCredentials,
  useSpotifyCredentials,
} from '@/api/integrations';
import StoredCredentialRow, { type CredentialField } from '@/components/layout/StoredCredentialRow.vue';
import { Button } from '@/components/ui/button';
import type { PanelState } from '@/composables/usePanelState';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

const lastCharacters = 4;

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const credentials = useSpotifyCredentials(channelId);
const integrations = useIntegrations(channelId);
const save = useSaveSpotifyCredentials(channelId);
const clear = useClearSpotifyCredentials(channelId);

const open = ref(false);
const error = ref<string | undefined>(undefined);

const stored = computed(() => credentials.data.value ?? null);
const ownClientId = computed(() => stored.value?.clientId ?? '');
const hasOwnApp = computed(() => ownClientId.value !== '' || stored.value?.hasClientSecret === true);
// The server says whether a shared app exists to fall back on. Without one, nothing offers it.
const hasSharedApp = computed(() => stored.value?.hasSharedApp === true);

const connected = computed(
  () => integrations.data.value?.find((row) => row.id === 'spotify')?.connected === true,
);

const state = computed<PanelState>(() => {
  if (credentials.isError.value)
    return 'error';

  return credentials.isPending.value || integrations.isPending.value
    ? 'loading'
    : 'ready';
});

const title = computed(() => {
  if (hasOwnApp.value)
    return t('dashboard.settings.integrations.spotify.ownApp');

  return hasSharedApp.value
    ? t('dashboard.settings.integrations.spotify.usingShared')
    : t('dashboard.settings.integrations.spotify.noApp');
});

const detail = computed(() => {
  if (hasOwnApp.value) {
    return t('dashboard.settings.integrations.spotify.ownDetail', {
      last4: ownClientId.value.slice(-lastCharacters),
    });
  }

  return hasSharedApp.value
    ? t('dashboard.settings.integrations.spotify.sharedDetail')
    : t('dashboard.settings.integrations.spotify.noAppDetail');
});

const editLabel = computed(() => {
  if (hasOwnApp.value)
    return t('dashboard.settings.integrations.spotify.edit');

  return hasSharedApp.value
    ? t('dashboard.settings.integrations.spotify.useOwn')
    : t('dashboard.settings.integrations.spotify.setUp');
});

const fields = computed<CredentialField[]>(() => [
  {
    name: 'clientId',
    label: t('dashboard.settings.integrations.spotify.clientId'),
    value: ownClientId.value,
  },
  {
    name: 'clientSecret',
    label: t('dashboard.settings.integrations.spotify.clientSecret'),
    secret: true,
    placeholder: stored.value?.hasClientSecret === true
      ? t('dashboard.settings.integrations.spotify.secretKept')
      : undefined,
  },
]);

watch(
  [state, connected, hasSharedApp, hasOwnApp],
  ([panel, isConnected, shared, own]) => {
    if (panel === 'ready' && !isConnected && !shared && !own) {
      open.value = true;
    }
  },
  { immediate: true },
);

async function submit(values: Record<string, string>): Promise<void> {
  error.value = undefined;

  try {
    await save.mutateAsync({
      clientId: (values.clientId ?? '').trim(),
      clientSecret: values.clientSecret ?? '',
    });

    open.value = false;
    toast.success(t('dashboard.settings.integrations.spotify.saved'));
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

async function remove(): Promise<void> {
  error.value = undefined;

  try {
    await clear.mutateAsync();
    open.value = false;

    toast.success(t(hasSharedApp.value
      ? 'dashboard.settings.integrations.spotify.clearedShared'
      : 'dashboard.settings.integrations.spotify.cleared'));
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}
</script>

<template>
  <p v-if="state === 'error'" role="alert" data-view="spotify-credentials" class="mt-1 text-sm text-destructive">
    {{ $t('dashboard.settings.integrations.spotify.loadFailed') }}
    <Button variant="outline" size="sm" type="button" @click="credentials.refetch()">
      {{ $t('dashboard.settings.integrations.spotify.retry') }}
    </Button>
  </p>

  <StoredCredentialRow
    v-else-if="state === 'ready'"
    v-model:open="open"
    inline
    :title="title"
    :detail="detail"
    :edit-label="editLabel"
    :dialog-title="$t('dashboard.settings.integrations.spotify.dialogTitle')"
    :dialog-description="hasSharedApp
      ? $t('dashboard.settings.integrations.spotify.descriptionShared')
      : $t('dashboard.settings.integrations.spotify.dialogDescription')"
    :save-label="$t('dashboard.settings.integrations.spotify.save')"
    :remove-label="hasSharedApp
      ? $t('dashboard.settings.integrations.spotify.clearShared')
      : $t('dashboard.settings.integrations.spotify.clear')"
    :fields="fields"
    :removable="hasOwnApp"
    :pending="save.isPending.value"
    :removing="clear.isPending.value"
    :error="error"
    remove-action="clear-spotify"
    @submit="submit"
    @remove="remove"
  />
</template>
