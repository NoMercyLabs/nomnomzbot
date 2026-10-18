<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's own Spotify app (client id and secret). The bot never sends the secret back: the page only
// learns that one is stored. Saving replaces both values; clearing falls back to the shared Spotify app only
// when the server reports one (hasSharedApp), and otherwise just removes the channel's own app.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useClearSpotifyCredentials, useSaveSpotifyCredentials, useSpotifyCredentials } from '@/api/integrations';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const credentials = useSpotifyCredentials(channelId);
const save = useSaveSpotifyCredentials(channelId);
const clear = useClearSpotifyCredentials(channelId);

const clientId = ref('');
const clientSecret = ref('');
const error = ref<string | undefined>(undefined);

const stored = computed(() => credentials.data.value ?? null);
// The server says whether a shared app exists to fall back on. Without one, nothing offers it.
const hasSharedApp = computed(() => stored.value?.hasSharedApp === true);
const canSave = computed(() => clientId.value.trim() !== '' && clientSecret.value !== '' && !save.isPending.value);

const state = computed<PanelState>(() => {
  if (credentials.isError.value)
    return 'error';

  return credentials.isPending.value
    ? 'loading'
    : 'ready';
});

watch(stored, (row) => {
  clientId.value = row?.clientId ?? '';
}, { immediate: true });

async function submit(): Promise<void> {
  error.value = undefined;

  try {
    await save.mutateAsync({ clientId: clientId.value.trim(), clientSecret: clientSecret.value });
    clientSecret.value = '';
    toast.success(t('dashboard.settings.integrations.spotify.saved'));
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

async function remove(): Promise<void> {
  error.value = undefined;

  try {
    await clear.mutateAsync();
    clientSecret.value = '';

    toast.success(t(hasSharedApp.value
      ? 'dashboard.settings.integrations.spotify.clearedShared'
      : 'dashboard.settings.integrations.spotify.cleared'));
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}
</script>

<template>
  <PanelCard
    data-view="spotify-credentials"
    pad
    :title="$t('dashboard.settings.integrations.spotify.title')"
    :description="hasSharedApp
      ? $t('dashboard.settings.integrations.spotify.descriptionShared')
      : $t('dashboard.settings.integrations.spotify.description')"
    :state="state"
    :error-text="$t('dashboard.settings.integrations.spotify.loadFailed')"
    @retry="credentials.refetch()"
  >
    <RowGroup>
      <form class="grid gap-3 px-3 py-2" @submit.prevent="submit">
        <label class="grid gap-1.5 text-sm font-medium">
          {{ $t('dashboard.settings.integrations.spotify.clientId') }}
          <Input v-model="clientId" name="clientId" autocomplete="off" />
        </label>
        <label class="grid gap-1.5 text-sm font-medium">
          {{ $t('dashboard.settings.integrations.spotify.clientSecret') }}
          <Input
            v-model="clientSecret"
            name="clientSecret"
            type="password"
            autocomplete="new-password"
            :placeholder="stored?.hasClientSecret ? $t('dashboard.settings.integrations.spotify.secretKept') : ''"
          />
        </label>
        <p v-if="error" role="alert" class="text-sm text-destructive">{{ error }}</p>
        <div class="flex flex-wrap justify-end gap-2">
          <Button
            v-if="stored?.hasClientSecret || stored?.clientId"
            type="button"
            variant="outline"
            size="sm"
            data-action="clear-spotify"
            :disabled="clear.isPending.value"
            @click="remove"
          >
            {{ hasSharedApp
              ? $t('dashboard.settings.integrations.spotify.clearShared')
              : $t('dashboard.settings.integrations.spotify.clear') }}
          </Button>
          <Button type="submit" size="sm" data-action="save-spotify" :disabled="!canSave">
            {{ $t('dashboard.settings.integrations.spotify.save') }}
          </Button>
        </div>
      </form>
    </RowGroup>
  </PanelCard>
</template>
