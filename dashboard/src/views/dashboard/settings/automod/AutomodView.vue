<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's four built-in AutoMod filters. The page edits a draft and sends the whole record in one save, so
// Save is the one primary action. A message a filter catches is deleted.
import { computed, ref, watch } from 'vue';
import { toast } from 'vue-sonner';
import { useI18n } from 'vue-i18n';

import { useAutomod, useSaveAutomod } from '@/api/automod';
import NumberRow from '@/components/layout/NumberRow.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Textarea } from '@/components/ui/textarea';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import TwitchAutomodPanel from './TwitchAutomodPanel.vue';
import {
  CAPS_RANGE,
  EMOTES_RANGE,
  problems,
  sameDraft,
  toConfig,
  toDraft,
  type AutomodDraft,
} from './automodDraft';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const config = useAutomod(channelId);
const save = useSaveAutomod(channelId);

const canManage = computed<boolean>(() => can('moderation:automod:write'));
const draft = ref<AutomodDraft | null>(null);
const saved = computed<AutomodDraft | null>(() =>
  config.data.value
    ? toDraft(config.data.value)
    : null);
const state = usePanelState(config);

// A fresh read replaces the draft only when the person has not edited it.
watch(
  saved,
  (next, previous) => {
    if (next === null) return;

    if (draft.value === null || !previous || sameDraft(draft.value, previous)) {
      draft.value = structuredClone(next);
    }
  },
  { immediate: true },
);

const dirty = computed<boolean>(
  () => draft.value !== null && saved.value !== null && !sameDraft(draft.value, saved.value),
);
const refused = computed(() =>
  draft.value
    ? problems(draft.value)
    : {});
const invalid = computed<boolean>(() => Object.keys(refused.value).length > 0);

function submit(): void {
  if (draft.value === null || !config.data.value || invalid.value) return;

  save.mutate(toConfig(draft.value, config.data.value), {
    onSuccess: () => toast.success(t('dashboard.settings.automod.saved')),
    onError: (error: Error) =>
      toast.error(t('dashboard.settings.automod.saveError', { reason: error.message })),
  });
}
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.automod')"
    :crumb="$t('dashboard.settings.automod.crumb')"
  >
    <template v-if="canManage" #actions>
      <Button
        size="sm"
        data-action="save"
        :disabled="!dirty || invalid || save.isPending.value"
        @click="submit"
      >
        {{ $t("dashboard.settings.automod.save") }}
      </Button>
    </template>
  </PageHeader>
  <PageBody>
    <p class="text-sm text-muted-foreground">{{ $t("dashboard.settings.automod.explain") }}</p>
    <PanelCard
      v-if="state !== 'ready' || draft === null"
      :title="$t('frame.nav.settingsPages.automod')"
      tier="quiet"
      pad
      :state="state === 'ready' ? 'loading' : state"
      :error-text="$t('dashboard.settings.automod.loadError')"
      @retry="config.refetch()"
    />
    <ReadOnlyScope v-else :read-only="!canManage">
      <div data-view="automod" class="grid gap-4">
        <PanelCard
          tier="quiet"
          pad
          data-group="links"
          :title="$t('dashboard.settings.automod.links.title')"
          :description="$t('dashboard.settings.automod.links.description')"
        >
          <RowGroup>
            <SettingRow
              v-model="draft.linksOn"
              data-field="linksOn"
              :title="$t('dashboard.settings.automod.links.enabled')"
            />
          </RowGroup>
          <Field>
            <div class="flex items-center gap-1.5">
              <FieldLabel for="automod-allowed">{{ $t("dashboard.settings.automod.links.allowed") }}</FieldLabel>
              <FieldInfo>{{ $t("dashboard.settings.automod.oneLine") }}</FieldInfo>
            </div>
            <Textarea
              id="automod-allowed"
              v-model="draft.allowedDomains"
              data-field="allowedDomains"
              rows="3"
            />
          </Field>
        </PanelCard>
        <PanelCard
          tier="quiet"
          pad
          data-group="caps"
          :title="$t('dashboard.settings.automod.caps.title')"
          :description="$t('dashboard.settings.automod.caps.description')"
        >
          <RowGroup>
            <SettingRow
              v-model="draft.capsOn"
              data-field="capsOn"
              :title="$t('dashboard.settings.automod.caps.enabled')"
            />
            <NumberRow
              v-model="draft.capsThreshold"
              data-field="capsThreshold"
              :title="$t('dashboard.settings.automod.caps.threshold')"
              :description="$t('dashboard.settings.automod.caps.thresholdHelp')"
              :min="CAPS_RANGE.min"
              :max="CAPS_RANGE.max"
              :error="refused.capsThreshold ? $t('dashboard.settings.automod.outOfRange', CAPS_RANGE) : undefined"
            />
          </RowGroup>
        </PanelCard>
        <PanelCard
          tier="quiet"
          pad
          data-group="phrases"
          :title="$t('dashboard.settings.automod.phrases.title')"
          :description="$t('dashboard.settings.automod.phrases.description')"
        >
          <RowGroup>
            <SettingRow
              v-model="draft.phrasesOn"
              data-field="phrasesOn"
              :title="$t('dashboard.settings.automod.phrases.enabled')"
            />
          </RowGroup>
          <Field>
            <div class="flex items-center gap-1.5">
              <FieldLabel for="automod-phrases">{{ $t("dashboard.settings.automod.phrases.list") }}</FieldLabel>
              <FieldInfo>{{ $t("dashboard.settings.automod.oneLine") }}</FieldInfo>
            </div>
            <Textarea id="automod-phrases" v-model="draft.phrases" data-field="phrases" rows="4" />
          </Field>
        </PanelCard>
        <PanelCard
          tier="quiet"
          pad
          data-group="emotes"
          :title="$t('dashboard.settings.automod.emotes.title')"
          :description="$t('dashboard.settings.automod.emotes.description')"
        >
          <RowGroup>
            <SettingRow
              v-model="draft.emotesOn"
              data-field="emotesOn"
              :title="$t('dashboard.settings.automod.emotes.enabled')"
            />
            <NumberRow
              v-model="draft.maxEmotes"
              data-field="maxEmotes"
              :title="$t('dashboard.settings.automod.emotes.max')"
              :description="$t('dashboard.settings.automod.emotes.maxHelp')"
              :min="EMOTES_RANGE.min"
              :max="EMOTES_RANGE.max"
              :error="refused.maxEmotes ? $t('dashboard.settings.automod.outOfRange', EMOTES_RANGE) : undefined"
            />
          </RowGroup>
        </PanelCard>
      </div>
    </ReadOnlyScope>
    <TwitchAutomodPanel v-if="can('moderation:automod:twitch:read')" />
  </PageBody>
</template>
