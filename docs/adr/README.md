# Architecture Decision Records

This directory records the load-bearing decisions for **Job Offer Matcher**, in the spirit of
constitution Principle XI (*Documented Decisions, Immutable History*). Each ADR is a short,
immutable note: once accepted it is not rewritten — a later decision supersedes it instead.

These ADRs are distilled from the feature plan and research:
`specs/001-job-offer-matcher/plan.md` (ADR-1/2/3) and `specs/001-job-offer-matcher/research.md`.

Template: **Title · Status · Context · Decision · Consequences.**

| ADR | Title | Status |
|-----|-------|--------|
| [0001](./0001-stack-lock.md) | Stack lock: React + .NET 10 + PostgreSQL web app | Accepted |
| [0002](./0002-source-access-accepted-risk.md) | Source access via the justjoin.it public JSON API (accepted risk) | Accepted |
| [0003](./0003-scheduler.md) | Scheduler: in-process `BackgroundService` + Cronos poll-tick | Accepted |
