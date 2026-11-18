<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Twitch permissions the product needs and the connection lacks (for example the ads and charity
// reads, which first-run setup does not ask for). The one button opens the guided re-grant dialog.
// Only a person who may read the Twitch diagnostics sees it. When the list cannot load, nothing shows
// and the page works as before.
import { useQuery } from '@tanstack/vue-query';
import { computed, ref } from 'vue';

import { missingScopesQuery } from '@/api/twitchHealth';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import TwitchRegrantDialog from '@/components/layout/TwitchRegrantDialog.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const { channelId } = useCurrentChannel();
const { can } = useCan();
const allowed = computed<boolean>(() => can('twitch:diagnostics:read') && Boolean(channelId.value));

const missing = useQuery(computed(() => ({
  ...missingScopesQuery(channelId.value ?? ''),
  enabled: allowed.value,
})));

const scopes = computed(() => missing.data.value?.scopes ?? []);
const guiding = ref(false);
</script>

<template>
  <InlineNotice
    v-if="allowed && scopes.length > 0"
    tone="warning"
    data-slot="missing-scopes"
    :title="$t('dashboard.settings.integrations.missingScopes.title', { count: scopes.length })"
  >
    <p class="m-0 text-muted-foreground">
      {{ $t('dashboard.settings.integrations.missingScopes.description') }}
    </p>
    <template #action>
      <Button variant="default" data-action="grant-scopes" @click="guiding = true">
        {{ $t('dashboard.settings.integrations.missingScopes.grant') }}
      </Button>
    </template>
  </InlineNotice>
  <TwitchRegrantDialog v-if="allowed" v-model:open="guiding" :scopes="scopes" />
</template>
