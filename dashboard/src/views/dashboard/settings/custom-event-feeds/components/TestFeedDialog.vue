<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Tries one feed without waiting for the source. "Send test event" fires the feed's custom event from a sample
// payload, so pipelines can be wired and seen at work. "Fetch now" reads the feed's address once and lists the
// key paths in the answer, so a field is chosen from the list and not typed blind.
import Download from '~icons/nnz/download';
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useFetchEventFeed, useTestEventFeed, type EventFeed } from '@/api/customEventFeeds';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { errorText } from '@/lib/errors';

const props = defineProps<{ feed: EventFeed | null }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const test = useTestEventFeed();
const fetcher = useFetchEventFeed();

const payload = ref('{}');
const error = ref<string | undefined>(undefined);
const sent = ref(false);
const keyPaths = ref<string[]>([]);
const truncated = ref(false);

watch(open, (shown) => {
  if (!shown) return;

  payload.value = '{}';
  error.value = undefined;
  sent.value = false;
  keyPaths.value = [];
  truncated.value = false;
});

async function send(): Promise<void> {
  if (props.feed === null) return;

  error.value = undefined;
  sent.value = false;

  try {
    await test.mutateAsync({ id: props.feed.id, samplePayload: payload.value });
    sent.value = true;
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

async function fetchNow(): Promise<void> {
  if (props.feed === null) return;

  error.value = undefined;

  try {
    const answer = await fetcher.mutateAsync(props.feed.id);

    keyPaths.value = answer.keyPaths;
    truncated.value = answer.truncated;
    payload.value = answer.rawJson;
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-dialog="test-feed"
    :title="$t('dashboard.settings.customEventFeeds.test.title', { name: feed?.displayName ?? '' })"
    :description="$t('dashboard.settings.customEventFeeds.test.description')"
    :action="$t('dashboard.settings.customEventFeeds.test.action')"
    :pending="test.isPending.value"
    :error="error"
    @submit="send"
  >
    <div class="grid gap-1.5">
      <span class="flex items-center justify-between gap-2 text-sm font-medium">
        {{ $t('dashboard.settings.customEventFeeds.test.payload') }}
        <Button
          v-if="feed?.endpointUrl"
          type="button"
          variant="outline"
          size="sm"
          data-fetch-now
          :disabled="fetcher.isPending.value"
          @click="fetchNow"
        >
          <Download class="size-4" />
          {{ $t('dashboard.settings.customEventFeeds.test.fetch') }}
        </Button>
      </span>
      <Textarea v-model="payload" data-field="payload" rows="6" class="font-mono" />
    </div>
    <div v-if="keyPaths.length > 0" data-key-paths class="grid gap-1.5">
      <span class="text-sm font-medium">{{ $t('dashboard.settings.customEventFeeds.test.paths') }}</span>
      <ul class="flex flex-wrap gap-1.5">
        <li
          v-for="path in keyPaths"
          :key="path"
          class="rounded-[6px] bg-muted px-2 py-0.5 font-mono text-sm"
        >
          {{ path }}
        </li>
      </ul>
      <span v-if="truncated" class="text-sm text-muted-foreground">
        {{ $t('dashboard.settings.customEventFeeds.test.truncated') }}
      </span>
    </div>
    <p v-if="sent" data-test-sent role="status" class="text-sm text-muted-foreground">
      {{ $t('dashboard.settings.customEventFeeds.test.sent', { name: feed?.name ?? '' }) }}
    </p>
  </FormDialog>
</template>
