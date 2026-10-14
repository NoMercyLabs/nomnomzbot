<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Data tab: what the bot keeps for this person beyond chat. Their consent and permits, the lines the bot
// uses for them, their command use, the quotes about them and any free-form data the channel saved.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PersonProfile } from '@/api/people';
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue';
import { shortDate } from '@/lib/time';

type Quote = PersonProfile['recentQuotes'][number];

const props = defineProps<{ profile: PersonProfile }>();

const { t, n, locale } = useI18n();

function dateOrNull(value: string | null | undefined): string | null {
  return value
    ? shortDate(value, locale.value)
    : null;
}

const consent = computed<KeyValueItem[]>(() => {
  const permits = props.profile.permits;
  // The API leaves a never-asked consent out of the answer, so missing and null both mean not asked.
  const granted = permits.ageConsentGranted ?? null;

  return [
    {
      label: t('profile.data.ageConsent'),
      value: granted === null
        ? t('profile.data.notAsked')
        : t(`profile.yesNo.${granted}`),
    },
    { label: t('profile.data.ageConsentOn'), value: dateOrNull(permits.ageConsentConfirmedUtc) },
    { label: t('profile.data.permits'), value: n(permits.activePermits.length) },
    {
      label: t('profile.data.analytics'),
      value: props.profile.activity?.isAnalyticsOptedOut
        ? t('profile.data.analyticsOff')
        : t('profile.data.analyticsOn'),
    },
  ];
});

const lines = computed<KeyValueItem[]>(() => [
  { label: t('profile.data.shoutout'), value: props.profile.overrides.shoutoutMessageTemplate },
  { label: t('profile.data.ttsVoice'), value: props.profile.overrides.ttsVoice?.voiceId },
]);

const commands = computed<KeyValueItem[]>(() => {
  const usage = props.profile.commandUsage;

  return [
    { label: t('profile.data.commandsUsed'), value: n(Number(usage.totalCommandsUsed)) },
    { label: t('profile.data.commandLast'), value: dateOrNull(usage.lastUsedUtc) },
    {
      label: t('profile.data.songRequests'),
      value: props.profile.activity
        ? n(Number(props.profile.activity.totalSongRequests))
        : null,
    },
  ];
});

const quotes = computed<Quote[]>(() => props.profile.recentQuotes);
const moreQuotes = computed<number>(() =>
  Number(props.profile.totalQuoteCount) - quotes.value.length);
</script>

<template>
  <div data-slot="profile-data" class="grid gap-6">
    <div class="grid gap-6 sm:grid-cols-2">
      <section class="grid content-start gap-2">
        <h3 class="text-sm font-semibold">{{ $t('profile.data.consent') }}</h3>
        <KeyValueList :items="consent" />
      </section>
      <section class="grid content-start gap-2">
        <h3 class="text-sm font-semibold">{{ $t('profile.data.commands') }}</h3>
        <KeyValueList :items="commands" />
      </section>
    </div>

    <section class="grid content-start gap-2">
      <h3 class="text-sm font-semibold">{{ $t('profile.data.lines') }}</h3>
      <KeyValueList :items="lines" />
    </section>

    <section class="grid content-start gap-2">
      <h3 class="text-sm font-semibold">{{ $t('profile.data.quotes') }}</h3>
      <p v-if="quotes.length === 0" class="text-sm text-muted-foreground">{{ $t('profile.data.noQuotes') }}</p>
      <ul v-else data-profile-quotes class="grid gap-1 text-sm">
        <li v-for="quote in quotes" :key="quote.id" class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3">
          <span class="text-muted-foreground tabular-nums">{{
            $t('profile.data.quoteNumber', { number: quote.number })
          }}</span>
          <span class="wrap-break-word">{{ quote.text }}</span>
        </li>
        <li v-if="moreQuotes > 0" class="text-muted-foreground">
          {{ $t('profile.data.moreQuotes', { count: moreQuotes }, moreQuotes) }}
        </li>
      </ul>
    </section>

    <section class="grid content-start gap-2">
      <h3 class="text-sm font-semibold">{{ $t('profile.data.saved') }}</h3>
      <KeyValueList
        v-if="profile.freeFormData.length > 0"
        :items="profile.freeFormData.map((datum) => ({ label: datum.key, value: datum.value }))"
      />
      <p v-else class="text-sm text-muted-foreground">{{ $t('profile.data.noSaved') }}</p>
    </section>
  </div>
</template>
