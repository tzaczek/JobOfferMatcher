export const meta = {
  name: 'backend-adversarial-review',
  description: 'Multi-dimension adversarial review of the Job Offer Matcher backend (US1–US3)',
  phases: [
    { title: 'Review', detail: 'one finder per dimension' },
    { title: 'Verify', detail: 'adversarial skeptics per finding' },
  ],
}

const ROOT = 'C:/Users/tomas/Repo/Job'
const SRC = `${ROOT}/backend/src`
const SPECS = `${ROOT}/specs/001-job-offer-matcher`

const CONTEXT = `
You are reviewing a .NET 10 backend (layered Domain→Application→Infrastructure→Web) for a local-first
job-offer aggregator. Source: ${SRC}. Specs/contracts: ${SPECS} (spec.md, data-model.md, research.md,
contracts/). Read the ACTUAL files (Read/Grep/Glob) — do not speculate.

Constitution rules to check: Domain has ZERO framework deps; deps point inward; NO raw Guid/int IDs in
Domain/Application (wrapped IDs only); NO static mutable singletons; NO .Result/.Wait()/.GetAwaiter().GetResult()
(async all the way); NO Console.WriteLine; NO anemic domain models / generic IRepository<T>; append-only
history (never edit applied migrations); value objects validate via Result<T> not exceptions; derived values
(NormalizedSalary, FitScore) NEVER persisted as fact (FR-035).

Report ONLY concrete, evidence-backed issues in THIS code — not style nits, not hypotheticals. For each,
give the exact file + line(s) and a minimal fix.`

const FINDINGS_SCHEMA = {
  type: 'object',
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          title: { type: 'string' },
          file: { type: 'string' },
          lines: { type: 'string' },
          severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low'] },
          category: { type: 'string' },
          description: { type: 'string' },
          evidence: { type: 'string' },
          suggestedFix: { type: 'string' },
        },
        required: ['title', 'file', 'severity', 'description', 'suggestedFix'],
      },
    },
  },
  required: ['findings'],
}

const VERDICT_SCHEMA = {
  type: 'object',
  properties: {
    real: { type: 'boolean' },
    confidence: { type: 'string', enum: ['high', 'medium', 'low'] },
    reasoning: { type: 'string' },
  },
  required: ['real', 'reasoning'],
}

const DIMENSIONS = [
  {
    key: 'orchestration',
    prompt: `${CONTEXT}\n\nDIMENSION: Scan orchestration correctness. Review ${SRC}/Application/Scanning/ScanOrchestrator.cs and the Domain Offer/OfferClassifier/ContentFingerprint. Check: new/updated/unchanged classification by identity existence + fingerprint diff (FR-012/013/014); disappearance reconciliation gated ONLY on a Complete collection with the <50% sanity guard (FR-015); reappearance handling; the per-offer SaveChanges + observation FK ordering; that source dates never decide new-vs-seen; single-flight correctness. Find real bugs.`,
  },
  {
    key: 'scoring-salary',
    prompt: `${CONTEXT}\n\nDIMENSION: Scoring + salary normalization correctness. Review ${SRC}/Domain/Matching/Scorer.cs, ${SRC}/Domain/Salary/SalaryNormalizer.cs + OfferSalaryReducer.cs, and ${SRC}/Infrastructure/Persistence/Repositories/OfferReadService.cs. Check: weights sum to 100 and axis math is bounded 0..1; FX/period/basis conversions; Result.Failure on missing amount/period/currency; ranking formulas (0.70·fit+0.30·salary; degraded 0.6·salary+0.4·recency); relative salary/recency score computation; that derived values are not persisted. Find real bugs.`,
  },
  {
    key: 'persistence',
    prompt: `${CONTEXT}\n\nDIMENSION: EF Core / persistence correctness. Review ${SRC}/Infrastructure/Persistence/** (AppDbContext, Configurations/*, Converters/*, Repositories/*, Migrations/*). Check: jsonb value converters + ValueComparer mutation tracking (esp. Currency round-trip, IReadOnlyList conversions); owned-type mappings (ExternalRef unique index, fingerprints); strongly-typed-id converters; single-row repos (FindAsync vs duplicate-add); query translation (ILike, in-memory vs SQL); migration chain consistency vs the model snapshot; UNIQUE(source_id,native_key) and UNIQUE(window_utc,trigger). Find real bugs.`,
  },
  {
    key: 'constitution',
    prompt: `${CONTEXT}\n\nDIMENSION: Constitution + layering compliance. Grep across ${SRC} for violations: raw Guid/int IDs in Domain/Application (outside Common/Ids and Infrastructure boundary); static mutable state; .Result/.Wait()/.GetAwaiter().GetResult(); Console.WriteLine/Debug.WriteLine; framework references leaking into Domain (it must reference no EF/ASP.NET/Microsoft.Extensions); generic IRepository<T>; anemic models. Also verify the scheduler BackgroundService resolves scoped services via a scope (not captured). Find real violations.`,
  },
  {
    key: 'requirements',
    prompt: `${CONTEXT}\n\nDIMENSION: Requirement coverage gaps. Cross-check the code against ${SPECS}/spec.md + contracts/. Verify: FR-010 hidden salary = empty band list (never zero); FR-031/SC-002 status persists + Dismissed never re-new + can't set status back to New; FR-026 graceful degradation when no readable CV; FR-040 403/challenge → Partial/ChallengeDetected + SourceBlocked; FR-007 polite pacing + detail-only behavior; the REST surface matches contracts/rest-api.md (offers/scans/schedule/cv/profile/settings). Report concrete missing or incorrect behaviors (not missing US4/export which are not built yet).`,
  },
  {
    key: 'concurrency-security',
    prompt: `${CONTEXT}\n\nDIMENSION: Concurrency, async, and security. Review the HTTP client (${SRC}/Infrastructure/Sources/JustJoinIt/*), scheduler (${SRC}/Infrastructure/Scheduling/*), single-flight guard, and Program.cs. Check: CancellationToken propagation; HttpClient typed-client lifetime + per-request pacing state (the _firstRequest field on a transient/shared client); generic non-PII User-Agent (Principle IV); no secrets/PII in code or logs; exception handler doesn't leak internals; potential races between scheduled + manual scans. Find real issues.`,
  },
]

phase('Review')
const reviews = await parallel(
  DIMENSIONS.map((d) => () =>
    agent(d.prompt, { label: `review:${d.key}`, phase: 'Review', schema: FINDINGS_SCHEMA, effort: 'high' })),
)

const allFindings = reviews
  .filter(Boolean)
  .flatMap((r, i) => (r.findings || []).map((f) => ({ ...f, dimension: DIMENSIONS[i].key })))

log(`Collected ${allFindings.length} candidate findings across ${DIMENSIONS.length} dimensions`)

phase('Verify')
const verified = await parallel(
  allFindings.map((f) => () =>
    parallel(
      ['correctness', 'spec-fidelity', 'does-it-actually-occur'].map((lens) => () =>
        agent(
          `${CONTEXT}\n\nADVERSARIAL VERIFY via the "${lens}" lens. A reviewer claims this issue in the code:\n` +
            `TITLE: ${f.title}\nFILE: ${f.file} ${f.lines || ''}\nSEVERITY: ${f.severity}\n` +
            `CLAIM: ${f.description}\nEVIDENCE: ${f.evidence || '(none given)'}\nPROPOSED FIX: ${f.suggestedFix}\n\n` +
            `Read the actual file(s) and try to REFUTE it. Is this a REAL, present defect that should be fixed? ` +
            `Default to real=false if the claim is speculative, already handled elsewhere, or a style preference.`,
          { label: `verify:${f.dimension}`, phase: 'Verify', schema: VERDICT_SCHEMA, effort: 'high' },
        ),
      ),
    ).then((votes) => {
      const real = votes.filter(Boolean).filter((v) => v.real).length
      return { finding: f, realVotes: real, totalVotes: votes.filter(Boolean).length, verdicts: votes.filter(Boolean) }
    }),
  ),
)

const confirmed = verified
  .filter(Boolean)
  .filter((v) => v.realVotes >= 2)
  .map((v) => ({
    title: v.finding.title,
    file: v.finding.file,
    lines: v.finding.lines,
    severity: v.finding.severity,
    dimension: v.finding.dimension,
    description: v.finding.description,
    suggestedFix: v.finding.suggestedFix,
    votes: `${v.realVotes}/${v.totalVotes}`,
  }))

const order = { critical: 0, high: 1, medium: 2, low: 3 }
confirmed.sort((a, b) => (order[a.severity] ?? 9) - (order[b.severity] ?? 9))

return {
  candidateCount: allFindings.length,
  confirmedCount: confirmed.length,
  confirmed,
}
