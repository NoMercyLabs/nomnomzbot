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
// reads, which first-run setup does not ask for). The one button starts the additive re-grant: Twitch
// shows a code the streamer approves, and the grant widens to the missing permissions. Only a person who
// may read the Twitch diagnostics sees it. When the list cannot load, nothing shows and the page works as before.
import { useQuery } from '@tanstack/vue-query';
import { computed } from 'vue';

import { missingScopesQuery, useStartRegrant } from '@/api/twitchHealth';
import InlineNotice from '@/components/layout/InlineNotice.vue';
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

const regrant = useStartRegrant();

const scopes = computed(() => missing.data.value?.scopes ?? []);
const code = computed(() => regrant.data.value ?? null);
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
    <ul class="m-0 mt-2 flex list-none flex-wrap gap-x-3 gap-y-1 p-0" data-slot="missing-scopes-list">
      <li v-for="item in scopes" :key="item.scope">
        <code class="font-mono text-xs">{{ item.scope }}</code>
      </li>
    </ul>
    <p v-if="code" class="m-0 mt-2" data-slot="missing-scopes-code">
      {{ $t('dashboard.settings.integrations.missingScopes.enterCode', { code: code.userCode }) }}
      <a
        :href="code.verificationUri"
        target="_blank"
        rel="noopener noreferrer"
        class="break-all underline underline-offset-4"
      >{{ code.verificationUri }}</a>
    </p>
    <InlineNotice v-if="regrant.isError.value" tone="error" compact class="mt-2">
      {{ $t('dashboard.settings.integrations.missingScopes.regrantFailed') }}
    </InlineNotice>
    <template v-if="!code" #action>
      <Button
        variant="default"
        data-action="grant-scopes"
        :disabled="regrant.isPending.value"
        @click="regrant.mutate()"
      >
        {{ $t('dashboard.settings.integrations.missingScopes.grant') }}
      </Button>
    </template>
  </InlineNotice>
</template>
