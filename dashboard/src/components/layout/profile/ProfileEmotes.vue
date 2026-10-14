<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The emotes a person's own channel offers, grouped by where they come from. Each one opens the same card a
// chat line opens: the emote large, its provider, kind and tier, and its own page.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePersonEmotes, type PersonEmote } from '@/api/community';
import AssetCardPopover from '@/components/layout/AssetCard.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import {
  catalogEmoteCard,
  emoteFrames,
  smallestUrl,
  type AssetCard,
} from '@/lib/assetCards';

const props = defineProps<{ userId: string }>();

const PROVIDERS = ['Twitch', 'SevenTv', 'Bttv', 'Ffz'] as const;

type ShownEmote = {
  code: string;
  src: string;
  card: AssetCard;
};

const { t } = useI18n();
const { channelId } = useCurrentChannel();

const emotes = usePersonEmotes(channelId, () =>
  props.userId);
const state = usePanelState(emotes, (list: PersonEmote[]) =>
  list.length === 0);

const groups = computed(() =>
  PROVIDERS.map((provider) => ({ provider, emotes: shown(provider) }))
    .filter((group) =>
      group.emotes.length > 0));

function shown(provider: string): ShownEmote[] {
  return (emotes.data.value ?? [])
    .filter((emote: PersonEmote) =>
      emote.provider === provider)
    .flatMap((emote: PersonEmote) => {
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
  <PanelCard
    data-slot="profile-emotes"
    pad
    :title="$t('profile.emotes.title')"
    :state="state"
    :empty-text="$t('profile.emotes.empty')"
    :error-text="$t('profile.emotes.error')"
    @retry="emotes.refetch()"
  >
    <div class="grid gap-4">
      <section
        v-for="group in groups"
        :key="group.provider"
        :data-profile-emote-group="group.provider"
        class="grid gap-2"
      >
        <h3 class="text-xs font-medium text-muted-foreground">
          {{ $t(`asset.providers.${group.provider}`) }}
        </h3>
        <ul class="flex flex-wrap gap-1">
          <li v-for="emote in group.emotes" :key="emote.code" :data-profile-emote="emote.code">
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
      </section>
    </div>
  </PanelCard>
</template>
