<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A list of emotes grouped by where they come from. Each one opens the same card a chat line opens: the emote
// large, its provider, kind and tier, and its own page. The moderator profile and the public profile share it.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PersonEmote } from '@/api/community';
import { emoteProviders } from '@/api/vocabulary';
import AssetCardPopover from '@/components/layout/AssetCard.vue';
import {
  catalogEmoteCard,
  emoteFrames,
  emoteTier,
  smallestUrl,
  type AssetCard,
} from '@/lib/assetCards';

const props = defineProps<{
  emotes: readonly PersonEmote[];
  // True when the bot has read the account and it is no affiliate or partner: it has no Twitch emotes.
  noTwitchEmotes: boolean;
}>();

type ShownEmote = {
  code: string;
  src: string;
  card: AssetCard;
};

type EmoteSet = {
  key: string;
  label: string | null;
  emotes: ShownEmote[];
};

const { t } = useI18n();

// Only an affiliate or a partner can have Twitch emotes; 7TV, BTTV and FFZ sets belong to anyone.
const regular = computed(() =>
  props.noTwitchEmotes);
const providers = computed(() =>
  emoteProviders.filter((provider) =>
    !regular.value || provider !== 'Twitch'));

const groups = computed(() =>
  providers.value.map((provider) => ({ provider, sets: sets(provider) }))
    .filter((group) =>
      group.sets.length > 0));

// A Twitch channel unlocks its emotes per sub tier, by cheering bits, or by following (owner 2026-10-14:
// "group channel emotes by tier and bits too"); the other sites have one set each.
function setKey(emote: PersonEmote): string {
  const tier = emoteTier(emote.tier);

  if (emote.type === 'subscriptions' && tier !== null)
    return `tier${tier}`;

  return emote.type === 'bitstier' || emote.type === 'follower'
    ? emote.type
    : 'other';
}

const SET_ORDER = ['follower', 'tier1', 'tier2', 'tier3', 'bitstier', 'other'] as const;

function sets(provider: string): EmoteSet[] {
  const list = props.emotes.filter((emote) =>
    emote.provider === provider);

  if (provider !== 'Twitch')
    return [{
      key: 'all',
      label: null,
      emotes: shown(list),
    }].filter((set) =>
      set.emotes.length > 0);

  return SET_ORDER.map((key) => ({
    key,
    label: t(`profile.emotes.sets.${key}`),
    emotes: shown(list.filter((emote) =>
      setKey(emote) === key)),
  })).filter((set) =>
    set.emotes.length > 0);
}

function shown(list: PersonEmote[]): ShownEmote[] {
  return list
    .flatMap((emote) => {
      const card = catalogEmoteCard(emote, t);
      const src = smallestUrl(emoteFrames(emote)) ?? card?.image;

      if (!card || !src)
        return [];

      return [{
        code: emote.code,
        src,
        card,
      }];
    });
}
</script>

<template>
  <div data-slot="profile-emote-groups" class="grid gap-4">
    <p v-if="regular" data-profile-emotes-regular class="text-sm text-muted-foreground">
      {{ $t('profile.emotes.regular') }}
    </p>
    <section
      v-for="group in groups"
      :key="group.provider"
      :data-profile-emote-group="group.provider"
      class="grid gap-3"
    >
      <h3 class="text-sm font-medium">
        {{ $t(`asset.providers.${group.provider}`) }}
      </h3>
      <div v-for="set in group.sets" :key="set.key" :data-profile-emote-set="set.key" class="grid gap-1">
        <h4 v-if="set.label" class="text-xs text-muted-foreground">{{ set.label }}</h4>
        <ul class="flex flex-wrap gap-1">
          <li v-for="emote in set.emotes" :key="emote.code" :data-profile-emote="emote.code">
            <AssetCardPopover
              :label="$t('asset.emoteLabel', { name: emote.code })"
              :title="emote.card.title"
              :image="emote.card.image"
              :facts="emote.card.facts"
              :people="emote.card.people"
              :link="emote.card.link"
            >
              <img :src="emote.src" :alt="emote.code" class="size-8 object-contain" />
            </AssetCardPopover>
          </li>
        </ul>
      </div>
    </section>
  </div>
</template>
