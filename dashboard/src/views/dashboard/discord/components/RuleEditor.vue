<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One notification rule: where the message goes, who it pings, what it says and the embed under it.
// Edits stay in a draft until Save. Preview renders the rule as stored, so it saves the draft first.
import { computed, ref, watch } from 'vue';
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

import BlockedChannels from './BlockedChannels.vue';
import EmbedPreview from './EmbedPreview.vue';
import PickerError from './PickerError.vue';
import { defaultChannelFor, readyGoLiveEmbed } from '../ruleDefaults';

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
  titleUrl: string;
  authorName: string;
  authorIconUrl: string;
  authorUrl: string;
  footerIconUrl: string;
  showTimestamp: boolean;
  buttonLabel: string;
  buttonUrl: string;
};

const props = defineProps<{
  trigger: string;
  placeholders: string[];
  rule: DiscordRule | null;
  connectionId: string;
  channels: DiscordChannel[];
  roles: DiscordRole[];
  pickerError: Error | null;
}>();

const emit = defineEmits<{ saved: [] }>();

const NO_ROLE = 'none';

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();
const save = useSaveDiscordRule(channelId, () => props.connectionId);
const create = useCreateDiscordRule(channelId, () => props.connectionId);
const preview = useDiscordPreview(channelId);

// A rule not yet saved starts with a ready message for its event, so a first save needs no typing.
function defaultMessage(): string {
  const key = `dashboard.discord.defaults.${props.trigger}`;

  return te(key)
    ? t(key)
    : '';
}

// A new go-live rule also starts with the ready embed. A saved rule keeps exactly what it stores.
function defaultEmbed(): DiscordEmbed | null {
  return props.trigger === 'go_live'
    ? readyGoLiveEmbed({
        author: t('dashboard.discord.defaults.goLiveAuthor'),
        game: t('dashboard.discord.defaults.goLiveGame'),
        button: t('dashboard.discord.defaults.goLiveButton'),
      })
    : null;
}

function draftOf(rule: DiscordRule | null) {
  const embed = rule === null
    ? defaultEmbed()
    : rule.embedConfig;

  return {
    enabled: rule?.enabled ?? true,
    targetChannelId: rule?.targetChannelId ?? defaultChannelFor(props.trigger, props.channels),
    pingRoleId: rule?.pingRoleId ?? NO_ROLE,
    message: rule === null
      ? defaultMessage()
      : (rule.messageTemplate ?? ''),
    title: embed?.title ?? '',
    description: embed?.description ?? '',
    color: embed?.color ?? '',
    thumbnailUrl: embed?.thumbnailUrl ?? '',
    imageUrl: embed?.imageUrl ?? '',
    footerText: embed?.footerText ?? '',
    fields: (embed?.fields ?? []).map((field) => ({ ...field })),
    titleUrl: embed?.titleUrl ?? '',
    authorName: embed?.authorName ?? '',
    authorIconUrl: embed?.authorIconUrl ?? '',
    authorUrl: embed?.authorUrl ?? '',
    footerIconUrl: embed?.footerIconUrl ?? '',
    showTimestamp: embed?.showTimestamp ?? false,
    buttonLabel: embed?.buttonLabel ?? '',
    buttonUrl: embed?.buttonUrl ?? '',
  } as const satisfies Draft;
}

const draft = ref<Draft>(draftOf(props.rule));
const shown = ref<DiscordPreview | null>(null);

// The server's channels arrive after the editor opens; a new rule picks its channel once they do.
watch(
  () => props.channels,
  (channels) => {
    if (props.rule === null && draft.value.targetChannelId === '') {
      draft.value.targetChannelId = defaultChannelFor(props.trigger, channels);
    }
  },
);

const placeholderList = computed<string>(() =>
  props.placeholders.map((name) => `{${name}}`).join(', '),
);

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
    fields: edits.fields.filter((field) => field.name.trim() !== ''),
    titleUrl: blankToNull(edits.titleUrl),
    authorName: blankToNull(edits.authorName),
    authorIconUrl: blankToNull(edits.authorIconUrl),
    authorUrl: blankToNull(edits.authorUrl),
    footerIconUrl: blankToNull(edits.footerIconUrl),
    showTimestamp: edits.showTimestamp,
    buttonLabel: blankToNull(edits.buttonLabel),
    buttonUrl: blankToNull(edits.buttonUrl),
  };
  const empty = Object.values(embed).every((value) =>
    value === null ||
    value === false ||
    (Array.isArray(value) && value.length === 0));

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
          >
            {{ t('dashboard.discord.editor.channelOption', { name: channel.name }) }}
          </SelectItem>
        </SelectContent>
      </Select>
      <BlockedChannels :channels="channels" />
      <PickerError :error="pickerError" />
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
      <PickerError :error="pickerError" />
    </Field>

    <TemplateField
      v-model="draft.message"
      :label="t('dashboard.discord.editor.message')"
      context="discord"
    />
    <p
      v-if="placeholders.length > 0"
      class="text-xs text-muted-foreground"
      data-field="placeholders"
    >
      {{ t('dashboard.discord.editor.placeholders', { list: placeholderList }) }}
    </p>

    <fieldset class="flex flex-col gap-3 rounded-[8px] border border-border p-3">
      <legend class="px-1 text-sm font-medium">
        {{ t('dashboard.discord.editor.embed') }}
      </legend>
      <TemplateField
        v-model="draft.title"
        :label="t('dashboard.discord.editor.embedTitle')"
        context="discord"
        data-field="embed-title"
        :rows="1"
        :maxlength="256"
        :panel="false"
      />
      <TemplateField
        v-model="draft.titleUrl"
        :label="t('dashboard.discord.editor.embedTitleUrl')"
        context="discord"
        data-field="embed-title-url"
        :rows="1"
        :panel="false"
      />
      <TemplateField
        v-model="draft.description"
        :label="t('dashboard.discord.editor.embedDescription')"
        context="discord"
        data-field="embed-description"
        :rows="3"
        :maxlength="4096"
      />
      <Field>
        <FieldLabel>{{ t('dashboard.discord.editor.embedColor') }}</FieldLabel>
        <Input v-model="draft.color" data-field="embed-color" placeholder="#9146FF" />
      </Field>
      <TemplateField
        v-model="draft.authorName"
        :label="t('dashboard.discord.editor.embedAuthorName')"
        context="discord"
        data-field="embed-author-name"
        :rows="1"
        :maxlength="256"
        :panel="false"
      />
      <TemplateField
        v-model="draft.authorIconUrl"
        :label="t('dashboard.discord.editor.embedAuthorIcon')"
        context="discord"
        data-field="embed-author-icon"
        :rows="1"
        :panel="false"
      />
      <TemplateField
        v-model="draft.authorUrl"
        :label="t('dashboard.discord.editor.embedAuthorUrl')"
        context="discord"
        data-field="embed-author-url"
        :rows="1"
        :panel="false"
      />
      <TemplateField
        v-model="draft.thumbnailUrl"
        :label="t('dashboard.discord.editor.embedThumbnail')"
        context="discord"
        data-field="embed-thumbnail"
        :rows="1"
        :panel="false"
      />
      <TemplateField
        v-model="draft.imageUrl"
        :label="t('dashboard.discord.editor.embedImage')"
        context="discord"
        data-field="embed-image"
        :rows="1"
        :panel="false"
      />
      <TemplateField
        v-model="draft.footerText"
        :label="t('dashboard.discord.editor.embedFooter')"
        context="discord"
        data-field="embed-footer"
        :rows="1"
        :maxlength="2048"
        :panel="false"
      />
      <TemplateField
        v-model="draft.footerIconUrl"
        :label="t('dashboard.discord.editor.embedFooterIcon')"
        context="discord"
        data-field="embed-footer-icon"
        :rows="1"
        :panel="false"
      />
      <SettingRow
        v-model="draft.showTimestamp"
        :title="t('dashboard.discord.editor.embedTimestamp')"
        :description="t('dashboard.discord.editor.embedTimestampHelp')"
      />
      <TemplateField
        v-model="draft.buttonLabel"
        :label="t('dashboard.discord.editor.buttonLabel')"
        context="discord"
        data-field="embed-button-label"
        :rows="1"
        :maxlength="80"
        :panel="false"
      />
      <TemplateField
        v-model="draft.buttonUrl"
        :label="t('dashboard.discord.editor.buttonUrl')"
        context="discord"
        data-field="embed-button-url"
        :rows="1"
        :panel="false"
      />

      <ul class="flex flex-col gap-2">
        <li
          v-for="(field, index) in draft.fields"
          :key="index"
          data-embed-field
          class="flex items-start gap-2"
        >
          <TemplateField
            v-model="field.name"
            class="flex-1"
            :label="t('dashboard.discord.editor.fieldName')"
            context="discord"
            :rows="1"
            :maxlength="256"
            :panel="false"
          />
          <TemplateField
            v-model="field.value"
            class="flex-1"
            :label="t('dashboard.discord.editor.fieldValue')"
            context="discord"
            :rows="1"
            :maxlength="1024"
            :panel="false"
          />
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
      <EmbedPreview v-if="shown.renderedEmbed" :embed="shown.renderedEmbed" />
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
