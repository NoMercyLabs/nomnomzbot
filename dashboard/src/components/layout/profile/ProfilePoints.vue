<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Points tab: this person's channel points wallet and their giveaways, as the bot stores them.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PersonProfile } from '@/api/people';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import { shortDate } from '@/lib/time';

const props = defineProps<{ profile: PersonProfile }>();

const { t, n, locale } = useI18n();

const wallet = computed<KeyValueItem[]>(() => {
  const account = props.profile.economy.wallet;

  if (!account)
    return [];

  return [
    { label: t('profile.points.balance'), value: n(Number(account.balance)) },
    { label: t('profile.points.earned'), value: n(Number(account.lifetimeEarned)) },
    { label: t('profile.points.spent'), value: n(Number(account.lifetimeSpent)) },
    {
      label: t('profile.points.frozen'),
      value: t(`profile.yesNo.${account.isFrozen}`),
    },
    {
      label: t('profile.points.lastActivity'),
      value: account.lastActivityAt
        ? shortDate(account.lastActivityAt, locale.value)
        : null,
    },
  ];
});

const games = computed<KeyValueItem[]>(() => {
  const economy = props.profile.economy;

  return [
    { label: t('profile.points.giveawayEntries'), value: n(Number(economy.giveawayEntryCount)) },
    { label: t('profile.points.giveawayWins'), value: n(Number(economy.giveawayWinCount)) },
    {
      label: t('profile.points.leaderboard'),
      value: economy.leaderboardOptedOut
        ? t('profile.points.leaderboardHidden')
        : t('profile.points.leaderboardShown'),
    },
  ];
});
</script>

<template>
  <div data-slot="profile-points" class="grid gap-6 sm:grid-cols-2">
    <section class="grid content-start gap-2">
      <h3 class="text-sm font-semibold">{{ $t('profile.points.wallet') }}</h3>
      <KeyValueList v-if="wallet.length > 0" :items="wallet" />
      <p v-else class="text-sm text-muted-foreground">{{ $t('profile.points.noWallet') }}</p>
    </section>
    <section class="grid content-start gap-2">
      <h3 class="text-sm font-semibold">{{ $t('profile.points.games') }}</h3>
      <KeyValueList :items="games" />
    </section>
  </div>
</template>
