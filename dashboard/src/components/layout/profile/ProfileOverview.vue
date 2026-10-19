<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Overview tab: the person's own About text, then what they are to this channel, then the small identity facts.
// The header already shows the name, pronouns, Twitch status, roles, account and follow age and first and last
// seen, so nothing here repeats them. A fact the server does not return is left out, never shown as a guess.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PersonProfile } from '@/api/people';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import { tierNumber } from '@/components/layout/profile/profileRoles';
import { Skeleton } from '@/components/ui/skeleton';
import { shortDate } from '@/lib/time';

// pending: the stored profile is still loading, so the panel shows a skeleton instead of an empty box.
const props = defineProps<{
  profile: PersonProfile | null;
  pending?: boolean;
  // owner: this person is the channel's own account.
  owner?: boolean;
}>();

const { t, locale } = useI18n();

type LinkedIdentity = PersonProfile['identity']['linkedIdentities'][number];

function present(item: KeyValueItem): boolean {
  return item.value !== undefined && item.value !== null && item.value !== '';
}

// The other platforms this person signed in with, by name and link date: "Kick: name, linked May 6, 2026".
function linked(identities: readonly LinkedIdentity[]): string | null {
  const names = identities
    .filter((identity) => identity.provider.toLowerCase() !== 'twitch')
    .map((identity) =>
      t('profile.facts.linkedOn', {
        provider: identity.provider,
        name: identity.providerDisplayName ?? identity.providerUsername,
        date: shortDate(identity.linkedAtUtc, locale.value),
      }),
    );

  return names.length > 0
    ? names.join('; ')
    : null;
}

const about = computed<string | null>(() => props.profile?.identity.description || null);

// The stored role as words. Only the channel's own account is the Broadcaster; a stored Broadcaster row on
// anyone else is stale data (the same rule roleKeys follows), so it is left out, not shown as a guess.
const managesAs = computed<string | null>(() => {
  const stored = props.profile?.identity.managementRole?.toLowerCase();

  if (!stored || (stored === 'broadcaster' && !props.owner))
    return null;

  return t(`profile.manager.${stored}`);
});

const relationship = computed<KeyValueItem[]>(() => {
  const known = props.profile?.identity;
  const tier = tierNumber(known?.subTier);
  const bits = Number(props.profile?.activity?.totalBitsCheered ?? 0);
  const raids = props.profile?.raids;

  return [
    {
      // Only someone who said !lurk is marked; a quiet viewer is not lurking by choice.
      label: t('profile.facts.lurking'),
      value: known?.isLurking
        ? t('profile.yesNo.true')
        : null,
    },
    {
      label: t('profile.facts.subscription'),
      value: tier === null
        ? null
        : t('profile.tier', { tier }),
    },
    {
      label: t('profile.facts.bestStreak'),
      value: props.profile?.streak
        ? t('profile.streakDays', Number(props.profile.streak.maxStreak))
        : null,
    },
    {
      label: t('profile.facts.bits'),
      value: bits > 0
        ? bits.toLocaleString(locale.value)
        : null,
    },
    {
      // Completed raids from the journaled channel.raid rows; zero means none were recorded, so no row.
      label: t('profile.facts.raidedUs'),
      value: raids?.timesRaidedThisChannel
        ? Number(raids.timesRaidedThisChannel).toLocaleString(locale.value)
        : null,
    },
    {
      label: t('profile.facts.weRaided'),
      value: raids?.timesThisChannelRaidedThem
        ? Number(raids.timesThisChannelRaidedThem).toLocaleString(locale.value)
        : null,
    },
    {
      label: t('profile.facts.manages'),
      value: managesAs.value,
    },
    {
      // memberSinceUtc is when this channel gave the person their role (ChannelMembership.GrantedAt).
      label: t('profile.facts.roleSince'),
      value: known?.memberSinceUtc
        ? shortDate(known.memberSinceUtc, locale.value)
        : null,
    },
  ].filter(present);
});

const identity = computed<KeyValueItem[]>(() => {
  const known = props.profile?.identity;

  return [
    { label: t('profile.facts.nickname'), value: known?.nickname },
    { label: t('profile.facts.chatColor'), value: known?.chatColor },
    { label: t('profile.facts.timezone'), value: known?.timezone },
    { label: t('profile.facts.linked'), value: linked(known?.linkedIdentities ?? []) },
    { label: t('profile.facts.twitchId'), value: known?.twitchUserId },
  ].filter(present);
});

// The chat color row shows the color itself next to its code.
const colorLabel = computed<string>(() => t('profile.facts.chatColor'));
</script>

<template>
  <div data-slot="profile-overview" class="grid gap-6">
    <div v-if="pending" data-profile-overview-loading class="grid gap-2">
      <Skeleton class="h-16 rounded-lg" />
      <Skeleton class="h-24 rounded-lg" />
    </div>
    <p v-else-if="!profile" data-profile-overview-empty class="text-sm text-muted-foreground">
      {{ $t("profile.overview.empty") }}
    </p>
    <p v-if="about" data-profile-about class="text-sm text-pretty">{{ about }}</p>

    <div class="grid grid-cols-[minmax(6rem,max-content)_minmax(0,1fr)] gap-x-4 gap-y-6">
      <section
        v-if="relationship.length > 0"
        class="col-span-full grid grid-cols-subgrid content-start gap-y-2"
      >
        <h3 class="col-span-full text-sm font-semibold">
          {{ $t("profile.sections.relationship") }}
        </h3>
        <KeyValueList :items="relationship" subgrid />
      </section>
      <section
        v-if="identity.length > 0"
        class="col-span-full grid grid-cols-subgrid content-start gap-y-2"
      >
        <h3 class="col-span-full text-sm font-semibold">{{ $t("profile.sections.identity") }}</h3>
        <KeyValueList :items="identity" subgrid>
          <template #value="{ item }">
            <span v-if="item.label === colorLabel" class="inline-flex items-center gap-2">
              <span
                data-profile-color
                class="size-3 flex-none rounded-full border"
                :style="{ backgroundColor: String(item.value) }"
              />
              {{ item.value }}
            </span>
            <template v-else>{{ item.value }}</template>
          </template>
        </KeyValueList>
      </section>
    </div>
  </div>
</template>
