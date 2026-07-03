# TTS, Stream Tools, Admin & Dev-Mode — Design (DRAFT)

Source: design dialogue 2026-06-16 (quick-sweep of the smaller systems).

## TTS
**Cost model (the clever part):**
- **Zero-cost path (all tiers + self-host):** edge/browser TTS rendered **client-side in the OBS TTS widget** (Edge TTS / Web Speech) → zero server cost. This is the default; there is no free *hosted* tier.
- **Premium voices (ElevenLabs / Azure):** **BYOK** — user provides their own API key; they pay their provider directly.
- **Self-host:** server-side TTS, operator's own keys/resources.

**Safety:** the real filter is **AutoMod, upstream** — Twitch AutoMod (streamer-configured) already blocks slurs/hate/TOS-violations before a message ever reaches TTS, so TTS leans on that, **not** a heavy duplicate filter. The TTS-level **profanity censor is opt-out** (a light swear filter you can disable — mild swearing is fine/funny). Optional **mod-approval queue** for cautious streamers. Don't duplicate AutoMod.

## Stream tools (title / game / tags)
- **Role-gated edits** (title/game/tags floor at **Editor** — Twitch defines our defaults: Editors edit stream info, mods only moderate chat) + **per-game presets/templates** + **scheduled changes**. Helix-backed.

## Platform admin surface (Plane C IAM)
- **Full ops console** for NoMercy Labs operators: tenant management, **audited support access** (`tenant:access`, logged), billing, feature flags, abuse/moderation. Gated by Plane C IAM (least-privilege, audited).

## IPC developer mode
- **Local socket**, **opt-in (off by default)**, **key-gated** (optional key). Tokenless local hook-in for developers building against the bot on the same machine. **Never exposed remotely.**
