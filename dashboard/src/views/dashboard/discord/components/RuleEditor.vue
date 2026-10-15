<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One notification rule: where the message goes, who it pings, what it says and the embed under it.
// Edits stay in a draft until Save. Preview renders the rule as stored, so it saves the draft first.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import Cross from '~icons/nnz/cross';

import {
  useCreateDiscordRule,
  useDiscordPreview,
  useSaveDiscordRule,
  type DiscordChannel,
  type DiscordEmbed,
  type DiscordEmbedField,
  type DiscordPreview,
  type DiscordRole,
  type DiscordRule,
} from '@/api/discord';
import SettingRow from '@/components/layout/SettingRow.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

type Draft = {
  enabled: boolean;
  targetChannelId: string;
  pingRoleId: string;
  message: string;
  title: string;
  description: string;
  color: string;
  thumbnailUrl: string;
  imageUrl: string;
  footerText: string;
  fields: DiscordEmbedField[];
};

const props = defineProps<{
  trigger: string;
  rule: DiscordRule | null;
  connectionId: string;
  channels: DiscordChannel[];
  roles: DiscordRole[];
}>();

const emit = defineEmits<{ saved: [] }>();

const NO_ROLE = 'none';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const save = useSaveDiscordRule(channelId, () =>
  props.connectionId);
const create = useCreateDiscordRule(channelId, () =>
  props.connectionId);
const preview = useDiscordPreview(channelId);

function draftOf(rule: DiscordRule | null): Draft {
  return {
    enabled: rule?.enabled ?? true,
    targetChannelId: rule?.targetChannelId ?? '',
    pingRoleId: rule?.pingRoleId ?? NO_ROLE,
    message: rule?.messageTemplate ?? '',
    title: rule?.embedConfig?.title ?? '',
    description: rule?.embedConfig?.description ?? '',
    color: rule?.embedConfig?.color ?? '',
    thumbnailUrl: rule?.embedConfig?.thumbnailUrl ?? '',
    imageUrl: rule?.embedConfig?.imageUrl ?? '',
    footerText: rule?.embedConfig?.footerText ?? '',
    fields: (rule?.embedConfig?.fields ?? []).map((field: DiscordEmbedField) => ({ ...field })),
  };
}

const draft = ref<Draft>(draftOf(props.rule));
const shown = ref<DiscordPreview | null>(null);

const failure = computed<Error | null>(
  () => save.error.value ?? create.error.value ?? preview.error.value,
);
const busy = computed<boolean>(() => save.isPending.value || create.isPending.value);
const canSave = computed<boolean>(() => draft.value.targetChannelId !== '' && !busy.value);

function blankToNull(value: string): string | null {
  return value.trim() === ''
    ? null
    : value.trim();
}

function embedOf(edits: Draft): DiscordEmbed | null {
  const embed: DiscordEmbed = {
    title: blankToNull(edits.title),
    description: blankToNull(edits.description),
    color: blankToNull(edits.color),
    thumbnailUrl: blankToNull(edits.thumbnailUrl),
    imageUrl: blankToNull(edits.imageUrl),
    footerText: blankToNull(edits.footerText),
    fields: edits.fields.filter((field: DiscordEmbedField) => field.name.trim() !== ''),
  };
  const empty = Object.values(embed).every((value) =>
    value === null || (Array.isArray(value) && value.length === 0));

  return empty
    ? null
    : embed;
}

function roleOf(edits: Draft): string | null {
  return edits.pingRoleId === NO_ROLE
    ? null
    : edits.pingRoleId;
}

// Saves the draft and answers the stored rule, which a first save creates.
async function persist(): Promise<DiscordRule | null> {
  const edits = draft.value;
  const body = {
    enabled: edits.enabled,
    targetChannelId: edits.targetChannelId,
    pingRoleId: roleOf(edits),
    messageTemplate: blankToNull(edits.message),
    embedConfig: embedOf(edits),
    milestoneType: props.rule?.milestoneType ?? null,
    milestoneThreshold: props.rule?.milestoneThreshold ?? null,
  };

  if (props.rule !== null)
    return save.mutateAsync({ configId: props.rule.id, change: body });

  return create.mutateAsync({ ...body, triggerType: props.trigger });
}

async function saveRule(): Promise<void> {
  await persist();
  emit('saved');
}

async function previewRule(): Promise<void> {
  const stored = await persist();

  if (stored !== null) {
    shown.value = await preview.mutateAsync(stored.id);
  }
}

function addField(): void {
  draft.value.fields.push({
    name: '',
    value: '',
    inline: false,
  });
}

function removeField(index: number): void {
  draft.value.fields.splice(index, 1);
}
</script>

<template>
  <form class="flex flex-col gap-5" data-rule-editor @submit.prevent="saveRule">
    <div class="rounded-[8px] border border-border p-1">
      <SettingRow
        v-model="draft.enabled"
        :title="t('dashboard.discord.editor.enabled')"
        :description="t('dashboard.discord.editor.enabledHelp')"
      />
    </div>

    <Field>
      <FieldLabel>{{ t('dashboard.discord.editor.channel') }}</FieldLabel>
      <Select v-model="draft.targetChannelId">
        <SelectTrigger data-field="channel" class="w-full">
          <SelectValue :placeholder="t('dashboard.discord.editor.channelPlaceholder')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem
            v-for="channel in channels"
            :key="channel.id"
            :value="channel.id"
            :disabled="!channel.canPost"
          >
            {{ t('dashboard.discord.editor.channelOption', { name: channel.name }) }}
          </SelectItem>
        </SelectContent>
      </Select>
    </Field>

    <Field>
      <FieldLabel>{{ t('dashboard.discord.editor.role') }}</FieldLabel>
      <Select v-model="draft.pingRoleId">
        <SelectTrigger data-field="role" class="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="NO_ROLE">
            {{ t('dashboard.discord.editor.noRole') }}
          </SelectItem>
          <SelectItem v-for="role in roles" :key="role.id" :value="role.id">
            {{ t('dashboard.discord.editor.roleOption', { name: role.name }) }}
          </SelectItem>
        </SelectContent>
      </Select>
    </Field>

    <TemplateField
      v-model="draft.message"
      :label="t('dashboard.discord.editor.message')"
      context="eventResponse"
      :event-type="trigger"
    />

    <fieldset class="flex flex-col gap-3 rounded-[8px] border border-border p-3">
      <legend class="px-1 text-sm font-medium">
        {{ t('dashboard.discord.editor.embed') }}
      </legend>
      <Field>
        <FieldLabel>{{ t('dashboard.discord.editor.embedTitle') }}</FieldLabel>
        <Input v-model="draft.title" data-field="embed-title" />
      </Field>
      <Field>
        <FieldLabel>{{ t('dashboard.discord.editor.embedDescription') }}</FieldLabel>
        <Input v-model="draft.description" data-field="embed-description" />
      </Field>
      <Field>
        <FieldLabel>{{ t('dashboard.discord.editor.embedColor') }}</FieldLabel>
        <Input v-model="draft.color" data-field="embed-color" placeholder="#9146FF" />
      </Field>
      <Field>
        <FieldLabel>{{ t('dashboard.discord.editor.embedThumbnail') }}</FieldLabel>
        <Input v-model="draft.thumbnailUrl" data-field="embed-thumbnail" />
      </Field>
      <Field>
        <FieldLabel>{{ t('dashboard.discord.editor.embedImage') }}</FieldLabel>
        <Input v-model="draft.imageUrl" data-field="embed-image" />
      </Field>
      <Field>
        <FieldLabel>{{ t('dashboard.discord.editor.embedFooter') }}</FieldLabel>
        <Input v-model="draft.footerText" data-field="embed-footer" />
      </Field>

      <ul class="flex flex-col gap-2">
        <li
          v-for="(field, index) in draft.fields"
          :key="index"
          data-embed-field
          class="flex items-center gap-2"
        >
          <Input v-model="field.name" :aria-label="t('dashboard.discord.editor.fieldName')" />
          <Input v-model="field.value" :aria-label="t('dashboard.discord.editor.fieldValue')" />
          <Button
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="t('dashboard.discord.editor.removeField')"
            @click="removeField(index)"
          >
            <Cross class="size-5" aria-hidden="true" />
          </Button>
        </li>
      </ul>
      <Button type="button"
              variant="outline"
              size="sm"
              class="self-start"
              data-add-field
              @click="addField"
      >
        {{ t('dashboard.discord.editor.addField') }}
      </Button>
    </fieldset>

    <section v-if="shown" data-preview class="flex flex-col gap-2 rounded-[8px] bg-muted p-3 text-sm">
      <p v-if="shown.pingRoleMention" data-preview-ping class="font-medium">
        {{ shown.pingRoleMention }}
      </p>
      <p data-preview-content class="wrap-break-word">
        {{ shown.renderedContent }}
      </p>
      <div
        v-if="shown.renderedEmbed"
        data-preview-embed
        class="border-l-4 border-primary pl-3"
      >
        <p v-if="shown.renderedEmbed.title" class="font-medium">
          {{ shown.renderedEmbed.title }}
        </p>
        <p v-if="shown.renderedEmbed.description">
          {{ shown.renderedEmbed.description }}
        </p>
        <p v-if="shown.renderedEmbed.footerText" class="text-xs text-muted-foreground">
          {{ shown.renderedEmbed.footerText }}
        </p>
      </div>
    </section>

    <p v-if="failure" role="alert" data-rule-error class="text-sm text-destructive">
      {{ errorText(failure, t) }}
    </p>

    <div class="flex items-center justify-end gap-2">
      <Button type="button"
              variant="outline"
              :disabled="!canSave"
              data-preview-button
              @click="previewRule"
      >
        {{ t('dashboard.discord.editor.preview') }}
      </Button>
      <Button type="submit" :disabled="!canSave" data-save-rule>
        {{ t('dashboard.discord.editor.save') }}
      </Button>
    </div>
  </form>
</template>
