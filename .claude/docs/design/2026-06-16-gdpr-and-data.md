# GDPR & Data Foundation — Design (DRAFT)

Source: design dialogue 2026-06-16, decisions locked via Q&A. Pairs with the roles/permissions + custom-command docs. Closes red-team data-isolation gaps.

## Multi-tenancy / data isolation
- **SaaS (Postgres):** **RLS + EF Core global query filters** — defense-in-depth. `ITenantScoped` filter bound to `CurrentTenantService` **and** Postgres Row-Level Security (`SET app.tenant_id` per connection). Even a missed `WHERE` can't leak rows.
- **Self-host (SQLite):** no RLS available → app-level query filters only. Selected by deployment profile.
- Closes red-team gap #2; pairs with the shipped Gate 1 fix (tenant id derived from the authenticated principal, not request input).

## Lawful basis
- **Viewers** (never signed up): **legitimate interest** — standard for chat-bot / channel operation. Honor **opt-out + erasure** on request.
- **Account holders** (streamers / mods): consent + contract via ToS + privacy-policy acceptance at signup.

## What counts as personal data (scope)
Under GDPR this is **not** just the email. The **Twitch username and user ID** are personal data (online identifiers that single out a person), as are **chat messages** and **behavioral data** (watch time, redemptions, etc.). Public ≠ exempt. The good news: almost all of it is **low-sensitivity** (not special-category), so the standard obligations here cover it — no extra safeguards. **Exception: pronouns** may count as data revealing gender identity → special-category (Art. 9), wanting extra care / explicit consent. Upshot: erasure + anonymization are *required* (usernames/messages are erasable PII), but treated as low-sensitivity, not like passwords/payment data.

## Erasure (right to be forgotten)
- **Tokens:** **crypto-shred** — destroy the per-tenant DEK → all token ciphertext unrecoverable, backups included (O(1)).
- **PII:** **irreversible anonymization** — strip/hash identifiers so rows can't be re-linked. NOT reversible pseudonymization. Anonymous aggregates may be retained (no longer personal data under GDPR).
- **Scope:** **whole deployment** — SaaS = every channel the subject appears in; self-host = that instance.
- **Cascade-safe:** the Twitch user ID links the FK graph, so erasure **consistently hashes the ID everywhere in one transaction** (joins + aggregates survive) + scrubs username / display-name / message content (+ shreds encrypted email/tokens). Person becomes unidentifiable; relationships + counts stay intact. Cleaner in the rebuild: an **internal surrogate key** for FKs so anonymization never touches the relationship graph.

## Retention
- **Data is stored permanently.** No auto-purge, no retention windows, no age-out, never tiered. Chat logs, events, stats, and audit rows are kept indefinitely.
- **The only deletion is manual GDPR erasure-on-request** — the subject (or a controller on their behalf) asks, and the erasure flow crypto-shreds the subject DEK + irreversibly anonymizes their PII. Nothing is removed on a timer.

## Data-subject rights UX
- **Self-service, automated "my-data" page:** export + delete, no human in the loop.
- **Export = machine-readable JSON** (data portability).

## Web / cookies (SaaS)
- **Essential-only, no tracking cookies** → no consent banner. Privacy by default. (If analytics ever needed, use cookieless.)

## Hosting / residency (SaaS)
- **EU-region** hosting — GDPR-native, data stays in the EU.

## Controller / processor roles
- **SaaS:** NoMercy Labs = **processor**; the streamer = **controller** of their channel's data. Requires a DPA + **sub-processor disclosure** (Twitch, hosting provider, etc.).
- **Self-host:** the operator is **both controller and processor**; NoMercy Labs is neither. The software *provides* the GDPR tooling (export, erasure, audit); the self-hoster *operates* it and carries the legal responsibility.

## Audit & breach
- Privileged / cross-tenant access (Plane C IAM) is **logged** (who / what / when / why). Erasure + export actions are themselves audited.
- **SaaS:** documented breach-notification process (72-hour GDPR window). **Self-host:** operator's responsibility, with logs/tooling to support it.
