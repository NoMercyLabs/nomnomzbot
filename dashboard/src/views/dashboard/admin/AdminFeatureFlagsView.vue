<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Platform feature flags: every definition, the editor for one, and the per-channel overrides.
// A change to an existing flag shows its counted reach first and saves only after a second, explicit confirm.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';
import Edit from '~icons/nnz/edit';
import Trash from '~icons/nnz/trash';

import {
  useAdminFeatureFlagOverrides,
  useAdminFeatureFlags,
  useDeleteFlagOverride,
  useFlagBlastRadius,
  useSaveAdminFeatureFlag,
  useSetFlagOverride,
  type FeatureFlag,
  type FeatureFlagOverride,
  type SetFeatureFlagRequest,
} from '@/api/adminFeatureFlags';
import { ApiError } from '@/api/errors';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { useTime } from '@/composables/useTime';

type Draft = {
  key: string;
  description: string;
  isEnabledGlobally: boolean;
  rolloutPercentage: number | '';
  minTierKey: string;
  requiresConsent: string;
  deploymentMode: string;
};

const blankDraft = (): Draft => ({
  key: '',
  description: '',
  isEnabledGlobally: false,
  rolloutPercentage: 100,
  minTierKey: '',
  requiresConsent: '',
  deploymentMode: '',
});

const { t } = useI18n();
const time = useTime();
const { can } = usePlatformCan();
const copy = (key: string, values: Record<string, string | number> = {}): string =>
  t(`dashboard.admin.featureFlags.${key}`, values);

const flags = useAdminFeatureFlags();
const overrides = useAdminFeatureFlagOverrides();
const save = useSaveAdminFeatureFlag();
const blast = useFlagBlastRadius();
const setOverride = useSetFlagOverride();
const removeOverride = useDeleteFlagOverride();

const draft = ref(blankDraft());
const editing = ref<string | null>(null);
const editorOpen = ref(false);
const confirming = ref(false);

const overrideChannel = ref('');
const overrideEnabled = ref(false);
const overrideReason = ref('');
const overrideExpiresAt = ref('');

const flagsState = usePanelState(flags);
const overridesState = usePanelState(overrides);

const forbidden = computed<boolean>(
  () => flags.error.value instanceof ApiError && flags.error.value.status === 403,
);

const flagsError = computed<string>(() =>
  forbidden.value
    ? copy('forbidden')
    : copy('loadError'),
);

const blank = (text: string): string | null =>
  text.trim() === ''
    ? null
    : text.trim();

const rolloutValid = computed<boolean>(() => {
  const share = draft.value.rolloutPercentage;

  return typeof share === 'number' && share >= 0 && share <= 100;
});

const canReview = computed<boolean>(
  () => draft.value.key.trim() !== '' && rolloutValid.value && !save.isPending.value,
);

const isNew = computed<boolean>(() => editing.value === null);

const text = (value: string | null | undefined): string => value ?? '';

function startNew(): void {
  draft.value = blankDraft();
  editing.value = null;
  editorOpen.value = true;
  confirming.value = false;
}

function startEdit(flag: FeatureFlag): void {
  draft.value = {
    key: flag.key,
    description: text(flag.description),
    isEnabledGlobally: flag.isEnabledGlobally,
    rolloutPercentage: Number(flag.rolloutPercentage),
    minTierKey: text(flag.minTierKey),
    requiresConsent: text(flag.requiresConsent),
    deploymentMode: text(flag.deploymentMode),
  };

  editing.value = flag.key;
  editorOpen.value = true;
  confirming.value = false;
}

function setRollout(value: string | number): void {
  draft.value.rolloutPercentage = value === ''
    ? ''
    : Number(value);
}

function body() {
  return {
    key: draft.value.key.trim(),
    description: blank(draft.value.description),
    isEnabledGlobally: draft.value.isEnabledGlobally,
    rolloutPercentage: Number(draft.value.rolloutPercentage),
    minTierKey: blank(draft.value.minTierKey),
    requiresConsent: blank(draft.value.requiresConsent),
    deploymentMode: blank(draft.value.deploymentMode),
  } as const satisfies SetFeatureFlagRequest;
}

function commit(): void {
  save.mutate(body(), {
    onSuccess: () => {
      toast.success(copy('saved'));
      confirming.value = false;
      editing.value = draft.value.key.trim();
    },
    onError: (error: Error) => toast.error(copy('saveError', { reason: error.message })),
  });
}

// A new flag reaches no channel yet, so it saves at once; an existing one shows its counted reach first.
function review(): void {
  if (isNew.value) {
    commit();

    return;
  }

  confirming.value = true;
  blast.mutate(draft.value.key.trim());
}

const blastNames = computed<string>(() => (blast.data.value?.sampleChannelNames ?? []).join(', '));

const canSaveOverride = computed<boolean>(
  () => editing.value !== null && overrideChannel.value.trim() !== '' && !setOverride.isPending.value,
);

function submitOverride(): void {
  if (editing.value === null) return;

  const expires = overrideExpiresAt.value === ''
    ? null
    : new Date(overrideExpiresAt.value).toISOString();

  setOverride.mutate(
    {
      flagKey: editing.value,
      broadcasterId: overrideChannel.value.trim(),
      body: {
        isEnabled: overrideEnabled.value,
        reason: blank(overrideReason.value),
        expiresAt: expires,
      },
    },
    {
      onSuccess: () => {
        toast.success(copy('overrideSaved'));
        overrideChannel.value = '';
        overrideReason.value = '';
        overrideExpiresAt.value = '';
      },
      onError: (error: Error) => toast.error(copy('overrideSaveError', { reason: error.message })),
    },
  );
}

function dropOverride(entry: FeatureFlagOverride): void {
  removeOverride.mutate(
    { flagKey: entry.flagKey, broadcasterId: entry.broadcasterId },
    {
      onSuccess: () => toast.success(copy('overrideRemoved')),
      onError: (error: Error) => toast.error(copy('overrideRemoveError', { reason: error.message })),
    },
  );
}

const whenOf = (entry: FeatureFlagOverride): string =>
  entry.expiresAt
    ? copy('overrideExpires', { when: time.dateTime(entry.expiresAt) })
    : copy('overrideNoExpiry');
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.featureFlags.title')" :crumb="$t('dashboard.admin.featureFlags.crumb')">
    <template #actions>
      <Button v-if="can('featureflag:write')" data-new variant="outline" @click="startNew">
        {{ $t('dashboard.admin.featureFlags.newFlag') }}
      </Button>
    </template>
  </PageHeader>
  <PageBody data-page-body>
    <p class="text-sm text-muted-foreground">{{ $t('dashboard.admin.featureFlags.explain') }}</p>
    <p data-restricted class="text-sm text-muted-foreground">{{ $t('dashboard.admin.featureFlags.restricted') }}</p>
    <PanelCard
      tier="lead"
      :title="$t('dashboard.admin.featureFlags.flagsTitle')"
      :state="flags.data.value?.length === 0 ? 'empty' : flagsState"
      :empty-text="$t('dashboard.admin.featureFlags.empty')"
      :error-text="flagsError"
      @retry="flags.refetch()"
    >
      <ul class="divide-y divide-border">
        <li
          v-for="flag in flags.data.value"
          :key="flag.key"
          :data-flag="flag.key"
          class="flex items-center gap-3 px-4 py-3"
        >
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <span class="text-sm font-medium wrap-break-word">{{ flag.key }}</span>
              <Badge :variant="flag.isEnabledGlobally ? 'default' : 'secondary'">
                {{ flag.isEnabledGlobally ? $t('dashboard.admin.featureFlags.on') : $t('dashboard.admin.featureFlags.off') }}
              </Badge>
              <span class="text-sm text-muted-foreground">
                {{ $t('dashboard.admin.featureFlags.rollout', { percent: Number(flag.rolloutPercentage) }) }}
              </span>
            </div>
            <p v-if="flag.description" class="text-sm text-muted-foreground">{{ flag.description }}</p>
          </div>
          <Button
            v-if="can('featureflag:write')"
            data-edit
            variant="outline"
            size="icon"
            :aria-label="$t('dashboard.admin.featureFlags.edit')"
            @click="startEdit(flag)"
          >
            <Edit class="size-5" aria-hidden="true" />
          </Button>
        </li>
      </ul>
    </PanelCard>
    <PanelCard
      v-if="editorOpen && can('featureflag:write')"
      data-editor
      :title="isNew ? $t('dashboard.admin.featureFlags.editorNew') : $t('dashboard.admin.featureFlags.editorEdit', { key: editing })"
    >
      <div class="flex flex-col gap-3 p-4">
        <div class="flex flex-col gap-1.5">
          <Label for="flag-key">{{ $t('dashboard.admin.featureFlags.fields.key') }}</Label>
          <Input id="flag-key" v-model="draft.key" data-field="key" :disabled="!isNew" />
        </div>
        <div class="flex flex-col gap-1.5">
          <Label for="flag-description">{{ $t('dashboard.admin.featureFlags.fields.description') }}</Label>
          <Input id="flag-description" v-model="draft.description" data-field="description" />
        </div>
        <div data-touch-target class="flex min-h-9 items-center justify-between gap-3">
          <Label for="flag-global">{{ $t('dashboard.admin.featureFlags.fields.isEnabledGlobally') }}</Label>
          <Switch id="flag-global" v-model="draft.isEnabledGlobally" data-field="isEnabledGlobally" />
        </div>
        <div class="flex flex-col gap-1.5">
          <Label for="flag-rollout">{{ $t('dashboard.admin.featureFlags.fields.rolloutPercentage') }}</Label>
          <Input
            id="flag-rollout"
            data-field="rolloutPercentage"
            type="number"
            min="0"
            max="100"
            :model-value="draft.rolloutPercentage"
            :aria-invalid="!rolloutValid"
            @update:model-value="setRollout"
          />
          <p v-if="!rolloutValid" role="alert" class="text-sm text-destructive">
            {{ $t('dashboard.admin.featureFlags.rolloutRange') }}
          </p>
        </div>
        <div class="flex flex-col gap-1.5">
          <Label for="flag-tier">{{ $t('dashboard.admin.featureFlags.fields.minTierKey') }}</Label>
          <Input id="flag-tier" v-model="draft.minTierKey" data-field="minTierKey" />
        </div>
        <div class="flex flex-col gap-1.5">
          <Label for="flag-consent">{{ $t('dashboard.admin.featureFlags.fields.requiresConsent') }}</Label>
          <Input id="flag-consent" v-model="draft.requiresConsent" data-field="requiresConsent" />
        </div>
        <div class="flex flex-col gap-1.5">
          <Label for="flag-mode">{{ $t('dashboard.admin.featureFlags.fields.deploymentMode') }}</Label>
          <Input id="flag-mode" v-model="draft.deploymentMode" data-field="deploymentMode" />
        </div>
        <div v-if="confirming" data-confirm-panel class="flex flex-col gap-2 rounded-md border border-border p-3">
          <span class="text-sm font-medium">{{ $t('dashboard.admin.featureFlags.confirmTitle') }}</span>
          <p v-if="blast.isPending.value" class="text-sm text-muted-foreground">
            {{ $t('dashboard.admin.featureFlags.blastLoading') }}
          </p>
          <p v-else-if="blast.isError.value" role="alert" class="text-sm text-destructive">
            {{ $t('dashboard.admin.featureFlags.blastError', { reason: blast.error.value?.message }) }}
          </p>
          <div v-else-if="blast.data.value" data-blast class="flex flex-col gap-1 text-sm">
            <p>{{ $t('dashboard.admin.featureFlags.blastCount', { count: Number(blast.data.value.tenantsAffected) }) }}</p>
            <p v-if="blastNames" class="text-muted-foreground">
              {{ $t('dashboard.admin.featureFlags.blastNames', { names: blastNames }) }}
            </p>
          </div>
        </div>
        <div class="flex justify-end gap-2">
          <Button v-if="confirming" variant="ghost" @click="confirming = false">
            {{ $t('dashboard.admin.featureFlags.back') }}
          </Button>
          <Button
            v-if="confirming"
            data-confirm
            :disabled="!blast.data.value || save.isPending.value"
            @click="commit"
          >
            {{ save.isPending.value ? $t('dashboard.admin.featureFlags.saving') : $t('dashboard.admin.featureFlags.confirm') }}
          </Button>
          <Button v-else data-review :disabled="!canReview" @click="review">
            {{ isNew ? $t('dashboard.admin.featureFlags.save') : $t('dashboard.admin.featureFlags.review') }}
          </Button>
        </div>
      </div>
    </PanelCard>
    <PanelCard
      tier="quiet"
      :title="editing === null ? $t('dashboard.admin.featureFlags.overridesTitle') : $t('dashboard.admin.featureFlags.overrideFormTitle', { key: editing })"
    >
      <div class="flex flex-col gap-3 p-4">
        <p v-if="editing === null" class="text-sm text-muted-foreground">
          {{ $t('dashboard.admin.featureFlags.overrideFormHint') }}
        </p>
        <template v-else-if="can('featureflag:write')">
          <div class="flex flex-col gap-1.5">
            <Label for="override-channel">{{ $t('dashboard.admin.featureFlags.overrideFields.channel') }}</Label>
            <Input id="override-channel" v-model="overrideChannel" data-field="overrideChannel" />
          </div>
          <div data-touch-target class="flex min-h-9 items-center justify-between gap-3">
            <Label for="override-enabled">{{ $t('dashboard.admin.featureFlags.overrideFields.enabled') }}</Label>
            <Switch id="override-enabled" v-model="overrideEnabled" data-field="overrideEnabled" />
          </div>
          <div class="flex flex-col gap-1.5">
            <Label for="override-reason">{{ $t('dashboard.admin.featureFlags.overrideFields.reason') }}</Label>
            <Input id="override-reason" v-model="overrideReason" data-field="overrideReason" />
          </div>
          <div class="flex flex-col gap-1.5">
            <Label for="override-expires">{{ $t('dashboard.admin.featureFlags.overrideFields.expiresAt') }}</Label>
            <Input
              id="override-expires"
              v-model="overrideExpiresAt"
              type="datetime-local"
              data-field="overrideExpiresAt"
            />
          </div>
          <div class="flex justify-end">
            <Button data-override-save variant="outline" :disabled="!canSaveOverride" @click="submitOverride">
              {{ $t('dashboard.admin.featureFlags.overrideSave') }}
            </Button>
          </div>
        </template>
      </div>
      <p
        v-if="overridesState === 'error'"
        role="alert"
        class="px-4 pb-3 text-sm text-destructive"
      >
        {{ $t('dashboard.admin.featureFlags.overridesError') }}
      </p>
      <p
        v-else-if="overrides.data.value?.length === 0"
        class="px-4 pb-3 text-sm text-muted-foreground"
      >
        {{ $t('dashboard.admin.featureFlags.overridesEmpty') }}
      </p>
      <ul class="divide-y divide-border border-t border-border">
        <li
          v-for="entry in overrides.data.value"
          :key="`${entry.flagKey}:${entry.broadcasterId}`"
          data-override
          class="flex items-center gap-3 px-4 py-3"
        >
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <span class="text-sm font-medium">{{ entry.flagKey }}</span>
              <span class="text-sm">{{ entry.channelName }}</span>
              <Badge :variant="entry.isEnabled ? 'default' : 'secondary'">
                {{ entry.isEnabled ? $t('dashboard.admin.featureFlags.overrideOn') : $t('dashboard.admin.featureFlags.overrideOff') }}
              </Badge>
            </div>
            <p v-if="entry.reason" class="text-sm text-muted-foreground">{{ entry.reason }}</p>
            <p class="text-sm text-muted-foreground">{{ whenOf(entry) }}</p>
          </div>
          <Button
            v-if="can('featureflag:write')"
            data-override-delete
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.admin.featureFlags.overrideDelete', { channel: entry.channelName })"
            @click="dropOverride(entry)"
          >
            <Trash class="size-5" aria-hidden="true" />
          </Button>
        </li>
      </ul>
    </PanelCard>
  </PageBody>
</template>
