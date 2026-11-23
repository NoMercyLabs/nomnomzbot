<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// How the channel gives a shoutout: whether the bot shouts out a known streamer on their first chat of a stream,
// and what it answers with: a message, or one target (pipeline, action or command) that runs instead. The
// switch saves at once. Save sends the message and the target; Use the default clears the message.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  useAutoShoutout,
  useSaveAutoShoutout,
  useSaveShoutoutTemplate,
  useShoutoutTemplate,
} from '@/api/shoutoutSettings';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReplyModeField from '@/components/layout/ReplyModeField.vue';
import type { ReplyMode } from '@/components/layout/replyMode';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { sameTarget, targetFromWire, targetToWire, type PickedTarget } from '@/lib/bindingTarget';
import { errorText } from '@/lib/errors';

import SettingsPageFrame from '../components/SettingsPageFrame.vue';

const TEMPLATE_MAX = 500;

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();

const auto = useAutoShoutout(channelId);
const template = useShoutoutTemplate(channelId);
const saveAuto = useSaveAutoShoutout(channelId);
const saveTemplate = useSaveShoutoutTemplate(channelId);

const canEdit = computed<boolean>(() => can('moderation:shoutout'));
const autoState = usePanelState(auto);
const templateState = usePanelState(template);

const enabled = computed<boolean>({
  get: () => auto.data.value?.enabled ?? false,
  set: (on: boolean) => changeAuto(on),
});

function changeAuto(on: boolean): void {
  saveAuto.mutate({ enabled: on }, {
    onError: (error: Error) => toast.error(errorText(error, t)),
  });
}

const stored = computed<string>(() => template.data.value?.template ?? '');
const storedTarget = computed<PickedTarget | null>(() =>
  targetFromWire(template.data.value?.target),
);
const text = ref('');
const mode = ref<ReplyMode>('simple');
const picked = ref<PickedTarget | null>(null);

// A fresh read replaces the text only when the person has not edited it.
watch(
  stored,
  (next, previous) => {
    if (text.value === (previous ?? '') || text.value === '') {
      text.value = next;
    }
  },
  { immediate: true },
);

// The same for the target: a fresh read replaces the choice only when the person has not changed it.
watch(
  storedTarget,
  (next, previous) => {
    if (sameTarget(picked.value, previous ?? null)) {
      picked.value = next;

      mode.value = next === null
        ? 'simple'
        : 'full';
    }
  },
  { immediate: true },
);

// A simple reply runs no target, so the choice sent is the picked target only in the full mode.
const chosen = computed<PickedTarget | null>(() =>
  mode.value === 'full'
    ? picked.value
    : null);
const targetChanged = computed<boolean>(() => !sameTarget(chosen.value, storedTarget.value));
const dirty = computed<boolean>(() => text.value.trim() !== stored.value || targetChanged.value);

// The target is sent only when it changed: a request without one leaves the saved target as it is.
function submit(message: string): void {
  const change = targetChanged.value
    ? { template: message, target: targetToWire(chosen.value) }
    : { template: message };

  saveTemplate.mutate(change, {
    onSuccess: () => toast.success(t('dashboard.settings.shoutouts.saved')),
    onError: (error: Error) => toast.error(errorText(error, t)),
  });
}

function useDefault(): void {
  text.value = '';
  submit('');
}
</script>

<template>
  <SettingsPageFrame :crumb="$t('dashboard.settings.shoutouts.crumb')" data-page-body>
    <PanelCard
      :title="$t('dashboard.settings.shoutouts.auto.title')"
      :description="$t('dashboard.settings.shoutouts.auto.description')"
      :state="autoState"
      :error-text="$t('dashboard.settings.shoutouts.auto.loadError')"
      @retry="auto.refetch()"
    >
      <RowGroup>
        <SettingRow
          v-model="enabled"
          data-field="autoShoutout"
          :title="$t('dashboard.settings.shoutouts.auto.label')"
          :description="$t('dashboard.settings.shoutouts.auto.help')"
          :disabled="!canEdit || saveAuto.isPending.value"
          :readonly="!canEdit"
        />
      </RowGroup>
    </PanelCard>
    <PanelCard
      tier="lead"
      pad
      :title="$t('dashboard.settings.shoutouts.message.title')"
      :description="$t('dashboard.settings.shoutouts.message.description')"
      :state="templateState"
      :error-text="$t('dashboard.settings.shoutouts.message.loadError')"
      @retry="template.refetch()"
    >
      <ReplyModeField
        v-model:mode="mode"
        v-model:target="picked"
        :kinds="['command', 'pipeline', 'action']"
        :new-pipeline-name="$t('dashboard.settings.shoutouts.message.title')"
        :disabled="!canEdit"
      >
        <TemplateField
          v-model="text"
          context="eventResponse"
          data-field="shoutoutTemplate"
          :label="$t('dashboard.settings.shoutouts.message.label')"
          :placeholder="$t('dashboard.settings.shoutouts.message.placeholder')"
          :maxlength="TEMPLATE_MAX"
          :rows="4"
          :disabled="!canEdit"
        />
      </ReplyModeField>
      <template v-if="canEdit && templateState === 'ready'" #footer>
        <Button
          size="sm"
          data-action="save"
          :variant="dirty ? 'default' : 'outline'"
          :disabled="!dirty || saveTemplate.isPending.value"
          @click="submit(text.trim())"
        >
          {{ $t("dashboard.settings.shoutouts.message.save") }}
        </Button>
        <Button
          variant="outline"
          size="sm"
          data-action="use-default"
          :disabled="stored === '' || saveTemplate.isPending.value"
          @click="useDefault"
        >
          {{ $t("dashboard.settings.shoutouts.message.useDefault") }}
        </Button>
      </template>
    </PanelCard>
  </SettingsPageFrame>
</template>
