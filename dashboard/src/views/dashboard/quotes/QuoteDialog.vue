<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// A new quote, or an edit of a saved one: the line, who said it, the game and when. The number is given by
// the server and never changes. A new quote also records the signed-in user as its author.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCurrentUser } from '@/api/auth';
import { useSaveQuote, type Quote } from '@/api/quotes';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

import { draftFromForm, formFromQuote, formProblem, maxQuoteLength, type QuoteForm } from './quoteForm';

const open = defineModel<boolean>('open', { required: true });
const props = defineProps<{ quote: Quote | null }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const save = useSaveQuote(channelId);
const user = useCurrentUser();

const form = ref<QuoteForm>(formFromQuote(props.quote));
const error = ref<string | undefined>(undefined);

const authorId = computed(() => user.data.value?.id ?? null);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  form.value = formFromQuote(props.quote);
  error.value = undefined;
});

async function submit(): Promise<void> {
  const problem = formProblem(form.value);

  error.value =
    problem === undefined
      ? undefined
      : t(`dashboard.quotes.dialog.problem.${problem}`, { max: maxQuoteLength });

  if (error.value !== undefined) return;

  try {
    await save.mutateAsync({
      existing: props.quote,
      draft: draftFromForm(form.value, authorId.value),
    });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');

    return;
  }

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="
      quote
        ? $t('dashboard.quotes.dialog.editTitle', { number: quote.number })
        : $t('dashboard.quotes.dialog.newTitle')
    "
    :description="$t('dashboard.quotes.dialog.description')"
    :action="quote ? $t('dashboard.quotes.dialog.save') : $t('dashboard.quotes.dialog.create')"
    :pending="save.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel for="quote-text">{{ $t("dashboard.quotes.dialog.text") }}</FieldLabel>
        <FieldInfo>{{ $t("dashboard.quotes.dialog.textHelp", { max: maxQuoteLength }) }}</FieldInfo>
      </div>
      <Textarea id="quote-text"
                v-model="form.text"
                data-quote-text
                rows="3"
                :maxlength="maxQuoteLength"
      />
    </Field>
    <div class="grid grid-cols-2 gap-3">
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="quote-said-by">{{ $t("dashboard.quotes.dialog.saidBy") }}</FieldLabel>
          <FieldInfo>{{ $t("dashboard.quotes.dialog.saidByHelp") }}</FieldInfo>
        </div>
        <Input id="quote-said-by" v-model="form.quotedDisplayName" data-quote-said-by autocomplete="off" />
      </Field>
      <Field>
        <FieldLabel for="quote-game">{{ $t("dashboard.quotes.dialog.game") }}</FieldLabel>
        <Input id="quote-game" v-model="form.contextGame" data-quote-game autocomplete="off" />
      </Field>
    </div>
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel for="quote-said-at">{{ $t("dashboard.quotes.dialog.saidAt") }}</FieldLabel>
        <FieldInfo>{{ $t("dashboard.quotes.dialog.saidAtHelp") }}</FieldInfo>
      </div>
      <Input id="quote-said-at" v-model="form.quotedAt" data-quote-said-at type="datetime-local" />
    </Field>
  </FormDialog>
</template>
