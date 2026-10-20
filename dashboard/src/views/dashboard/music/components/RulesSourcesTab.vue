<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Sources tab of the request rules: which services viewers may request from, who may use each one
// (the channel's role rule for that service), which service searches first, and the banger playlist.
// The edits live in the parent's draft; the role choice saves at once, as the permissions page does.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useMusicPlaylists } from '@/api/music';
import { rungOf, rungs, useActionMatrix, useSetActionLevel, type RungName } from '@/api/permissions';
import { musicProviders } from '@/api/vocabulary';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import OptionSelect, { type Option } from '@/views/dashboard/settings/permissions/components/OptionSelect.vue';

import RuleRow from './RuleRow.vue';
import RuleSection from './RuleSection.vue';
import { noPlaylist, playlistValue, type RuleDraft } from './requestRules';

const draft = defineModel<RuleDraft>({ required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const playlistsQuery = useMusicPlaylists(channelId);
const matrix = useActionMatrix(channelId);
const setLevel = useSetActionLevel(channelId);

const sourceActions = {
  spotify: 'music:request:spotify',
  youtube: 'music:request:youtube',
} as const satisfies Record<string, string>;

function sourceRung(actionKey: string): RungName | undefined {
  const row = matrix.data.value?.find((entry) => entry.actionKey === actionKey);

  return row
    ? rungOf(row.effectiveLevel)
    : undefined;
}

const rungOptions = computed<Option[]>(() =>
  rungs.map((rung) => ({
    value: rung.name,
    label: t(`dashboard.music.rules.sourceRole.${rung.name}`),
  })));

function setSourceRung(actionKey: string, rung: string): void {
  setLevel.mutate({ actionKey, rung: rung as RungName });
}
</script>

<template>
  <fieldset class="contents" :disabled="!can('music:config:write')">
    <div class="grid gap-5">
      <RuleSection :title="$t('dashboard.music.rules.sources')">
        <RowGroup>
          <RuleRow :title="$t('dashboard.music.rules.allowSpotify')">
            <GatedSwitch
              v-model="draft.allowSpotify"
              action="music:config:write"
              data-setting="allow-spotify"
            />
          </RuleRow>
          <RuleRow v-if="draft.allowSpotify" :title="$t('dashboard.music.rules.whoSpotify')">
            <OptionSelect
              :model-value="sourceRung(sourceActions.spotify)"
              :options="rungOptions"
              :label="$t('dashboard.music.rules.whoSpotify')"
              :disabled="!can('roles:manage') || setLevel.isPending.value"
              data-setting="who-spotify"
              @update:model-value="setSourceRung(sourceActions.spotify, $event)"
            />
          </RuleRow>
          <RuleRow :title="$t('dashboard.music.rules.allowYouTube')">
            <GatedSwitch
              v-model="draft.allowYouTube"
              action="music:config:write"
              data-setting="allow-youtube"
            />
          </RuleRow>
          <RuleRow v-if="draft.allowYouTube" :title="$t('dashboard.music.rules.whoYouTube')">
            <OptionSelect
              :model-value="sourceRung(sourceActions.youtube)"
              :options="rungOptions"
              :label="$t('dashboard.music.rules.whoYouTube')"
              :disabled="!can('roles:manage') || setLevel.isPending.value"
              data-setting="who-youtube"
              @update:model-value="setSourceRung(sourceActions.youtube, $event)"
            />
          </RuleRow>
          <RuleRow
            v-if="draft.allowYouTube"
            :title="$t('dashboard.music.rules.youTubeMusicOnly')"
            :description="$t('dashboard.music.rules.youTubeMusicOnlyHelp')"
          >
            <GatedSwitch
              v-model="draft.youTubeMusicCategoryOnly"
              action="music:config:write"
              data-setting="youtube-music-only"
            />
          </RuleRow>
        </RowGroup>
      </RuleSection>
      <RuleSection :title="$t('dashboard.music.rules.preferred')">
        <RadioGroup v-model="draft.preferredProvider" as-child class="gap-0">
          <RowGroup>
            <label
              v-for="provider in musicProviders"
              :key="provider"
              class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2 text-sm font-medium hover:bg-foreground/5"
            >
              <RadioGroupItem :value="provider" />
              {{ $t(`dashboard.music.rules.providers.${provider}`) }}
            </label>
          </RowGroup>
        </RadioGroup>
      </RuleSection>
      <RuleSection
        :title="$t('dashboard.music.rules.banger')"
        :description="$t('dashboard.music.rules.bangerHelp')"
      >
        <RowGroup>
          <RuleRow :title="$t('dashboard.music.rules.bangerPlaylist')">
            <Select v-model="draft.bangerPlaylist">
              <SelectTrigger
                data-setting="banger-playlist"
                :aria-label="$t('dashboard.music.rules.banger')"
                class="w-44"
              >
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem :value="noPlaylist">{{
                  $t("dashboard.music.rules.bangerAuto")
                }}</SelectItem>
                <SelectItem
                  v-for="playlist in playlistsQuery.data.value ?? []"
                  :key="playlist.id"
                  :value="playlistValue(playlist.provider, playlist.id)"
                >
                  {{ playlist.name }}
                </SelectItem>
              </SelectContent>
            </Select>
          </RuleRow>
          <RuleRow :title="$t('dashboard.music.rules.bangerAutoCreate')">
            <GatedSwitch
              v-model="draft.bangerAutoCreate"
              action="music:config:write"
              data-setting="banger-auto-create"
            />
          </RuleRow>
        </RowGroup>
      </RuleSection>
    </div>
  </fieldset>
</template>
