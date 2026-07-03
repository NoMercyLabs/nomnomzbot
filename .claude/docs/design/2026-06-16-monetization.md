# Monetization & Tiers — Design (DRAFT, directional)

Source: design dialogue 2026-06-16. Directional — numbers are indicative, not final.

## Model
- **Self-host: free, forever** (open-source, BYO infra). SaaS is the paid product.
- **SaaS: tiered subscription**, economies of scale — marginal cost/user drops as shared infra amortizes across users. Target steady-state ~$1 cost / ~$2+ charge per user.
- Tiers (final): **$3.99 base / $7.99 pro / $14.99 premium.** There is **no free hosted tier** — SaaS is paid-only from Base up; self-host is the only free path.
- **Tier limits are drawn around real cost drivers** — sandboxed execution time, widget/asset hosting, event-store retention, queue/quota sizes — so each tier covers its own cost (the "minimum per tier that doesn't lose money").

## No free hosted tier — generous entry instead
- **There is no $0 cloud plan.** SaaS is paid-only; the **only** free path is **self-host** (free forever, full features, unlimited).
- **The paid entry tier (Base) is generous** = every *core* feature works. Commands, basic widgets, moderation, SR, the essential bot.
- The paywall gates **scale + convenience + advanced + support**, **never core capability.**
- Why: self-host already covers anyone who wants free, so the hosted product sells *convenience* (no infra) from the first paid tier. Base is priced low ($3.99) to stay land-and-expand; convert up on scale / custom-bot / support.

## Premium gates
- Custom bot name (own bot identity).
- Higher limits / quotas (more widgets, longer history, bigger queues, more execution).
- Advanced features.
- Priority support / uptime SLA.
- **Not** AI — AI is never marketed as a selling point (it can power features quietly, never the headline).

## Launch
- **Invite-only** at the start — controlled scale while cost-per-user is high early on.
- **Founders badge** — cosmetic status perk for early invite adopters (loyalty + community).
