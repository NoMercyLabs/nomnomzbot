<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Overview tab (mockup profile.js panes().overview): Identity and Relationship as key-value lists and the
// latest 3 chat lines. A fact the server does not return is left out, never shown as a guess.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import ArrowRight from '~icons/nnz/arrow-right';

import type { CommunityMember } from '@/api/community';
import type { PersonProfile } from '@/api/people';
import EmoteText from '@/components/layout/EmoteText.vue';
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue';
import { tierNumber, type RoleKey } from '@/components/layout/profile/profileRoles';
import { Button } from '@/components/ui/button';
import { agoText, clockTime, shortDate } from '@/lib/time';

type ActivityLine = CommunityMember['recentActivity'][number];

const LATEST = 3;

const props = defineProps<{
  person: CommunityMember;
  profile: PersonProfile | null;
  roles: readonly RoleKey[];
}>();

defineEmits<{ showChat: [] }>();

const { t, locale } = useI18n();

type LinkedIdentity = PersonProfile['identity']['linkedIdentities'][number];

function present(item: KeyValueItem): boolean {
  return item.value !== undefined && item.value !== null && item.value !== '';
}

type Identity = PersonProfile['identity'];

// A person can set a second pronoun set on pronouns.alejo.io; both show, the main one first.
// A moderator can set them by hand instead, which the list says.
function pronouns(known: Identity | undefined): string | null {
  const sets = [known?.pronoun, known?.altPronoun]
    .filter((value): value is string => Boolean(value));

  if (sets.length === 0)
    return null;

  const text = sets.join(' · ');

  return known?.pronounIsManualOverride
    ? t('profile.facts.setByChannel', { value: text })
    : text;
}

// The other platforms this person signed in with, by name and link date: "Kick: name, linked May 6, 2026".
function linked(identities: readonly LinkedIdentity[]): string | null {
  const names = identities
    .filter((identity: LinkedIdentity) => identity.provider.toLowerCase() !== 'twitch')
    .map((identity: LinkedIdentity) => t('profile.facts.linkedOn', {
      provider: identity.provider,
      name: identity.providerDisplayName ?? identity.providerUsername,
      date: shortDate(identity.linkedAtUtc, locale.value),
    }));

  return names.length > 0
    ? names.join('; ')
    : null;
}

// Twitch sends "" for a plain account; affiliate, partner and the Twitch staff roles show in words.
function twitchStatus(known: Identity | undefined): string | null {
  const kinds = [known?.broadcasterType, known?.userType]
    .filter((kind): kind is string => Boolean(kind));
  const names = kinds.map((kind: string) => t(`profile.twitchStatus.${kind}`));

  return names.length > 0
    ? names.join(', ')
    : null;
}

function dateAndAgo(iso: string | null | undefined): string | null {
  if (!iso)
    return null;

  return `${shortDate(iso, locale.value)} (${agoText(iso, new Date(), locale.value)})`;
}

const identity = computed<KeyValueItem[]>(() => {
  const who = props.person;
  const known = props.profile?.identity;
  const follower = props.profile?.activity?.isFollower;

  return [
    { label: t('profile.facts.name'), value: `${who.displayName} (@${who.username})` },
    { label: t('profile.facts.nickname'), value: known?.nickname },
    { label: t('profile.facts.pronouns'), value: pronouns(known) },
    { label: t('profile.facts.accountCreated'), value: dateAndAgo(known?.accountCreatedAtUtc) },
    { label: t('profile.facts.twitchStatus'), value: twitchStatus(known) },
    { label: t('profile.facts.chatColor'), value: known?.chatColor },
    { label: t('profile.facts.timezone'), value: known?.timezone },
    { label: t('profile.facts.about'), value: known?.description },
    { label: t('profile.facts.linked'), value: linked(known?.linkedIdentities ?? []) },
    {
      label: t('profile.facts.manages'),
      value: known?.managementRole
        ? t(`profile.manager.${known.managementRole.toLowerCase()}`)
        : null,
    },
    {
      // memberSinceUtc is when this channel gave the person their role (ChannelMembership.GrantedAt).
      label: t('profile.facts.roleSince'),
      value: known?.memberSinceUtc
        ? shortDate(known.memberSinceUtc, locale.value)
        : null,
    },
    {
      label: t('profile.facts.following'),
      value: follower === undefined
        ? null
        : t(`profile.following.${follower}`),
    },
    {
      label: t('profile.facts.trust'),
      value: known
        ? t(`profile.standing.${known.communityStanding.toLowerCase()}`)
        : null,
    },
    { label: t('profile.facts.twitchId'), value: known?.twitchUserId },
  ].filter(present);
});

const roleText = computed<string>(() => {
  const names = props.roles.map((key: string) => t(`profile.roles.${key}`));

  return names.length > 0
    ? names.join(', ')
    : t('profile.roles.viewer');
});

const relationship = computed<KeyValueItem[]>(() => {
  const who = props.person;
  const first = props.profile?.identity.firstSeenUtc ?? who.firstSeen;
  const ago = agoText(who.lastSeen, new Date(), locale.value);
  const tier = tierNumber(props.profile?.identity.subTier);
  const bits = Number(props.profile?.activity?.totalBitsCheered ?? 0);

  return [
    {
      // Only someone who said !lurk is marked; a quiet viewer is not lurking by choice.
      label: t('profile.facts.lurking'),
      value: props.profile?.identity.isLurking
        ? t('profile.yesNo.true')
        : null,
    },
    { label: t('profile.facts.roles'), value: roleText.value },
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
    { label: t('profile.facts.firstSeen'), value: shortDate(first, locale.value) },
    { label: t('profile.facts.lastSeen'), value: `${shortDate(who.lastSeen, locale.value)} (${ago})` },
  ].filter(present);
});

const latest = computed<ActivityLine[]>(() => {
  const lines = [...props.person.recentActivity];

  lines.sort((a: ActivityLine, b: ActivityLine) => b.timestamp.localeCompare(a.timestamp));

  return lines.slice(0, LATEST);
});

// The chat color row shows the color itself next to its code.
const colorLabel = computed<string>(() => t('profile.facts.chatColor'));

const now = new Date();
</script>

<template>
  <div data-slot="profile-overview" class="grid gap-6">
    <div class="grid gap-6 sm:grid-cols-2">
      <section class="grid content-start gap-2">
        <h3 class="text-sm font-semibold">{{ $t('profile.sections.identity') }}</h3>
        <KeyValueList :items="identity">
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
      <section class="grid content-start gap-2">
        <h3 class="text-sm font-semibold">{{ $t('profile.sections.relationship') }}</h3>
        <KeyValueList :items="relationship" />
      </section>
    </div>

    <section class="grid gap-2">
      <div class="flex items-center justify-between gap-2">
        <h3 class="text-sm font-semibold">{{ $t('profile.chat.title') }}</h3>
        <Button
          v-if="latest.length > 0"
          data-profile-all-chat
          type="button"
          variant="ghost"
          size="sm"
          @click="$emit('showChat')"
        >
          {{ $t('profile.chat.all') }}
          <ArrowRight />
        </Button>
      </div>
      <p v-if="latest.length === 0" class="text-sm text-muted-foreground">
        {{ $t('profile.chat.empty') }}
      </p>
      <ul v-else class="grid gap-1">
        <li
          v-for="line in latest"
          :key="`${line.timestamp}-${line.content}`"
          data-profile-line
          class="grid grid-cols-[auto_minmax(0,1fr)] items-baseline gap-x-3 text-sm"
        >
          <span class="text-xs whitespace-nowrap text-muted-foreground tabular-nums">{{
            clockTime(line.timestamp, now, locale)
          }}</span>
          <EmoteText class="truncate" :title="line.content" :text="line.content" />
        </li>
      </ul>
    </section>
  </div>
</template>
