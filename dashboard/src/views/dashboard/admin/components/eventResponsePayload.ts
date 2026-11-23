// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

// An event response definition's payload is the portable part of a channel's event response:
// `{ "eventType": "channel.follow", "responseType": "chat_message", "message": "...", "metadata": { ... },
// "isEnabled": true }`. It holds no pipeline id: a pipeline response binds one of the installing channel's own
// pipelines at install time. The form edits the response type, the message and the switch; the event type is the
// fixed key of the row and the metadata travels as it came, so reading and writing change no part of the payload.
import type { JsonValue } from '@/lib/json';
import { objectOf } from '@/views/dashboard/overlays/components/settingField';

// The words of the fields belong to the event responses page; this form loads them itself (ensureWords).
export const eventResponseWords = (locale: string) =>
  import(`../../event-responses/i18n/${locale}.json`);

// The response types the server accepts (EventResponseTemplatePayload.ResponseTypes).
export const responseTypes = ['chat_message', 'overlay', 'pipeline', 'none'] as const;

export type ResponseType = (typeof responseTypes)[number];

const KEYS: readonly string[] = [
  'eventType',
  'responseType',
  'message',
  'metadata',
  'isEnabled',
] as const;

export type EventResponsePayload = {
  eventType: string;
  responseType: ResponseType;
  // The own text; empty means the channel keeps its default lines.
  message: string;
  metadata: Record<string, string>;
  isEnabled: boolean;
};

function parseJson(payloadJson: string): JsonValue | undefined {
  try {
    return JSON.parse(payloadJson) as JsonValue;
  } catch {
    return undefined;
  }
}

function metadataOf(value: JsonValue | undefined): Record<string, string> | null {
  const map = objectOf(value ?? {});

  if (map === null || !Object.values(map).every((entry) => typeof entry === 'string'))
    return null;

  return map as Record<string, string>;
}

function responseTypeOf(value: JsonValue | undefined): ResponseType | null {
  const given = value ?? 'chat_message';

  return responseTypes.find((candidate) => candidate === given) ?? null;
}

// The parts of the payload, or null when the payload holds anything the form would drop: another field, text that
// is not JSON, no event type, an unknown response type, a message that is not text, metadata that is not text
// pairs, a switch that is not true or false. The caller then shows the raw JSON.
export function readEventResponsePayload(payloadJson: string): EventResponsePayload | null {
  const parsed = objectOf(parseJson(payloadJson));

  if (parsed === null || !Object.keys(parsed).every((name) => KEYS.includes(name)))
    return null;

  const responseType = responseTypeOf(parsed.responseType);
  const metadata = metadataOf(parsed.metadata);
  const message = parsed.message ?? '';
  const isEnabled = parsed.isEnabled ?? true;

  if (
    typeof parsed.eventType !== 'string' ||
    parsed.eventType === '' ||
    responseType === null ||
    metadata === null ||
    typeof message !== 'string' ||
    typeof isEnabled !== 'boolean'
  )
    return null;

  return {
    eventType: parsed.eventType,
    responseType,
    message,
    metadata,
    isEnabled,
  };
}

// The payload the server reads: an empty message and empty metadata are left out.
export function writeEventResponsePayload(payload: EventResponsePayload): string {
  const hasMetadata = Object.keys(payload.metadata).length > 0;

  return JSON.stringify({
    eventType: payload.eventType,
    responseType: payload.responseType,
    message: payload.message === ''
      ? undefined
      : payload.message,
    metadata: hasMetadata
      ? payload.metadata
      : undefined,
    isEnabled: payload.isEnabled,
  });
}
