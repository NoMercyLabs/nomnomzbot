<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Whether Twitch events (follows, raids, subs) reach the bot, in plain words, with the one fix that applies.
// Needs a signed-in streamer; on a fresh install the check waits until after the first sign-in.
import { useQuery } from '@tanstack/vue-query';
import { computed, ref } from 'vue';

import { useSignedIn } from '@/api/auth';
import { myChannelsQuery } from '@/api/channels';
import {
  eventFeedFactsQuery,
  judgeEventFeed,
  useReconcileEventSub,
  type EventFeedVerdict,
} from '@/api/twitchHealth';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import TwitchRegrantDialog from '@/components/layout/TwitchRegrantDialog.vue';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';

import PublicWait from '../../components/PublicWait.vue';

const signedIn = useSignedIn();
const channels = useQuery({ ...myChannelsQuery, enabled: signedIn });
const channelId = computed(() => channels.data.value?.[0]?.id ?? '');
const facts = useQuery(computed(() => ({
  ...eventFeedFactsQuery(channelId.value),
  enabled: channelId.value !== '',
})));
const verdict = computed<EventFeedVerdict | null>(() =>
  facts.data.value
    ? judgeEventFeed(facts.data.value)
    : null);
const reconcile = useReconcileEventSub(channelId);
const guiding = ref(false);
const feedScopes = computed(() =>
  verdict.value?.state === 'missingScopes'
    ? verdict.value.scopes.map((scope) => ({
        scope,
        features: [],
        detectedAtRuntime: false,
        chatNotified: false,
      }))
    : []);

const tone = computed(() =>
  verdict.value?.state === 'ok'
    ? 'ok'
    : 'bad');
</script>

<template>
  <section class="flex flex-col gap-2 rounded-lg border p-4" aria-labelledby="setup-feed-title">
    <h3 id="setup-feed-title" class="text-sm font-medium">{{ $t('site.setup.feed.title') }}</h3>

    <p v-if="!signedIn" class="text-sm text-muted-foreground">
      {{ $t('site.setup.feed.signInFirst') }}
    </p>
    <PublicWait v-else-if="facts.isPending.value">{{ $t('site.setup.feed.checking') }}</PublicWait>
    <InlineNotice v-else-if="facts.isError.value || !verdict" tone="info" compact>
      {{ $t('site.setup.feed.unknown') }}
    </InlineNotice>
    <template v-else>
      <StatusBadge :tone="tone" :label="$t(`site.setup.feed.${verdict.state}`, verdict)" />

      <Button v-if="verdict.state === 'missingScopes'"
              variant="outline"
              class="self-start"
              data-action="feed-regrant"
              @click="guiding = true"
      >
        {{ $t('site.setup.feed.regrant') }}
      </Button>

      <div v-else-if="verdict.state !== 'ok'">
        <Button variant="outline" :disabled="reconcile.isPending.value" @click="reconcile.mutate()">
          <Spinner v-if="reconcile.isPending.value" />
          {{ $t('site.setup.feed.reconcile') }}
        </Button>
        <InlineNotice v-if="reconcile.isError.value" tone="error" compact class="mt-2">
          {{ $t('site.setup.feed.reconcileFailed') }}
        </InlineNotice>
      </div>
    </template>
    <!-- Outside the verdict branch: on approval the verdict turns ok, and the dialog stays to say so. -->
    <TwitchRegrantDialog v-if="signedIn" v-model:open="guiding" :scopes="feedScopes" />
  </section>
</template>
