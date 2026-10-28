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
import { computed } from 'vue';

import { useSignedIn } from '@/api/auth';
import { myChannelsQuery } from '@/api/channels';
import {
  eventFeedFactsQuery,
  judgeEventFeed,
  useReconcileEventSub,
  useStartRegrant,
  type EventFeedVerdict,
} from '@/api/twitchHealth';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';

import SetupAddress from './SetupAddress.vue';

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
const regrant = useStartRegrant();

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
    <p v-else-if="facts.isPending.value" class="text-sm text-muted-foreground" role="status">
      {{ $t('site.setup.feed.checking') }}
    </p>
    <p v-else-if="facts.isError.value || !verdict" class="text-sm text-muted-foreground" role="status">
      {{ $t('site.setup.feed.unknown') }}
    </p>
    <template v-else>
      <StatusBadge :tone="tone" :label="$t(`site.setup.feed.${verdict.state}`, verdict)" />

      <div v-if="verdict.state === 'missingScopes'" class="flex flex-col gap-2">
        <p class="text-sm text-muted-foreground">{{ verdict.scopes.join(', ') }}</p>
        <div v-if="regrant.data.value" class="flex flex-col gap-2 text-sm">
          {{ $t('site.setup.feed.regrantCode', { code: regrant.data.value.userCode }) }}
          <SetupAddress :url="regrant.data.value.verificationUri" use="both" />
        </div>
        <div class="flex gap-2">
          <Button variant="outline" :disabled="regrant.isPending.value" @click="regrant.mutate()">
            {{ $t('site.setup.feed.regrant') }}
          </Button>
          <Button v-if="regrant.data.value" variant="ghost" @click="facts.refetch()">
            {{ $t('site.setup.feed.recheck') }}
          </Button>
        </div>
      </div>

      <div v-else-if="verdict.state !== 'ok'">
        <Button variant="outline" :disabled="reconcile.isPending.value" @click="reconcile.mutate()">
          {{ $t('site.setup.feed.reconcile') }}
        </Button>
        <p v-if="reconcile.isError.value" class="mt-2 text-sm text-destructive" role="alert">
          {{ $t('site.setup.feed.reconcileFailed') }}
        </p>
      </div>
    </template>
  </section>
</template>
