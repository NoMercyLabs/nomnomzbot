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

// Whole seconds as m:ss, or h:mm:ss from one hour on (10-stream.html:353). Digits are never localised: a clock reads the same everywhere.
export function formatCountdown(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const rest: string = String(seconds % 60).padStart(2, '0');

  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, '0')}:${rest}`
    : `${minutes}:${rest}`;
}

// Whole seconds as m:ss with the minutes counted past 60 ("60:00"), the form parseClock reads back.
export function formatClock(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds));
  const rest: string = String(seconds % 60).padStart(2, '0');

  return `${Math.floor(seconds / 60)}:${rest}`;
}

// What a person types for a length: m:ss ("4:30") or bare digits, which are minutes ("4"). Anything else is NaN.
export function parseClock(text: string): number {
  const trimmed = text.trim();
  const clock = /^(\d+):([0-5]\d)$/.exec(trimmed);

  if (clock)
    return Number(clock[1]) * 60 + Number(clock[2]);

  return /^\d+$/.test(trimmed)
    ? Number(trimmed) * 60
    : Number.NaN;
}
