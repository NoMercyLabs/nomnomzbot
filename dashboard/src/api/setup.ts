// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { queryOptions, useMutation, useQueryClient } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { failed } from './errors';
import type { components } from './schema';

export type SetupWizard = components['schemas']['SetupWizard'];
export type SetupStep = components['schemas']['SetupStep'];
export type BotStatus = components['schemas']['BotStatus'];

const version = { path: { version: '1' } };

export const setupWizardKey = ['system', 'setup', 'wizard'] as const;
export const botStatusKey = ['system', 'setup', 'bot-status'] as const;

async function fetchWizard(): Promise<SetupWizard> {
  const { data, error, response } = await client.GET('/api/v{version}/system/setup/wizard', {
    params: version,
  });

  if (!response.ok || !data?.data)
    throw failed('setup wizard', response.status, error);

  return data.data;
}

// The server decides the steps and which of them are required; the page draws what it gets.
export const setupWizardQuery = queryOptions({
  queryKey: setupWizardKey,
  queryFn: fetchWizard,
});

export type CredentialValues = Record<string, string>;

// One typed call per credential step; the Twitch app is the only one whose secret may stay empty.
const credentialSavers = {
  twitch_app: (values: CredentialValues) =>
    client.PUT('/api/v{version}/system/setup/credentials/twitch', {
      params: version,
      body: {
        clientId: values.clientId ?? '',
        clientSecret: values.clientSecret ?? '',
      },
    }),
  spotify: (values: CredentialValues) =>
    client.PUT('/api/v{version}/system/setup/credentials/spotify', {
      params: version,
      body: { clientId: values.clientId ?? '', clientSecret: values.clientSecret ?? '' },
    }),
  discord: (values: CredentialValues) =>
    client.PUT('/api/v{version}/system/setup/credentials/discord', {
      params: version,
      body: { clientId: values.clientId ?? '', clientSecret: values.clientSecret ?? '' },
    }),
  youtube: (values: CredentialValues) =>
    client.PUT('/api/v{version}/system/setup/credentials/youtube', {
      params: version,
      body: { clientId: values.clientId ?? '', clientSecret: values.clientSecret ?? '' },
    }),
} as const;

export function hasCredentialForm(stepKey: string): stepKey is keyof typeof credentialSavers {
  return stepKey in credentialSavers;
}

export function useSaveCredentials(stepKey: MaybeRefOrGetter<string>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (values: CredentialValues): Promise<void> => {
      const key = toValue(stepKey);

      if (!hasCredentialForm(key))
        throw failed('setup save', 404);

      const { error, response } = await credentialSavers[key](values);

      if (!response.ok)
        throw failed('setup save', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: setupWizardKey }),
  });
}

// The one-click choice of the shared NomNomzBot Twitch app.
export function useUseSharedTwitchApp() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/system/setup/credentials/twitch/use-shared',
        { params: version },
      );

      if (!response.ok)
        throw failed('setup save', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: setupWizardKey }),
  });
}

// The address Twitch asks the person to approve the bot account at.
export async function fetchBotAuthorizeUrl(): Promise<string> {
  const { data, error, response } = await client.GET('/api/v{version}/system/setup/bot/oauth-url', {
    params: version,
  });

  if (!response.ok || !data?.data)
    throw failed('setup bot', response.status, error);

  return data.data.oauthUrl;
}

async function fetchBotStatus(): Promise<BotStatus> {
  const { data, error, response } = await client.GET('/api/v{version}/system/setup/bot/status', {
    params: version,
  });

  if (!response.ok || !data?.data)
    throw failed('setup bot', response.status, error);

  return data.data;
}

// While the person approves the account in another tab, the page asks again until it is connected.
export const botStatusQuery = queryOptions({
  queryKey: botStatusKey,
  queryFn: fetchBotStatus,
});

// The finish step: the bot marks first-run setup done and locks the credential writes.
export function useCompleteSetup() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/system/setup/complete', {
        params: version,
      });

      if (!response.ok)
        throw failed('setup complete', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: setupWizardKey }),
  });
}
