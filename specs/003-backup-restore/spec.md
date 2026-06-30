# Feature Specification: On-Demand Backup & Restore

**Feature Branch**: `003-backup-restore` (spec directory; no git branch created — repo has no `before_specify` hook)

**Created**: 2026-06-29

**Status**: Draft

**Input**: User description: "create backup for database which runs on demand and can be called from UI. Make sure no data is lost - the current data must stay as is and be backed up"

## Clarifications

### Session 2026-06-29

- Q: Should this feature also let the user restore from a backup, or only create backups (in a documented, restorable format)? → A: **Create + restore** — the feature delivers both on-demand backup creation and an on-demand restore from a supplied backup. Restore is destructive-by-nature, so it is guarded by validation, an automatic safety pre-backup, explicit confirmation, and all-or-nothing semantics.
- Q: Where should an on-demand backup be delivered/stored? → A: **Download in browser** — clicking "Backup" streams a single self-contained archive to the user's browser downloads (matching the existing `/export` UX; works whether the app runs on the host or in Docker). There is no server-side backup catalog in this feature; the user keeps the downloaded archives, and a restore is initiated by uploading a previously downloaded archive.
- Q: How should backup archives (which contain all personal data and CVs) be protected at rest? → A: **No encryption** — archives are treated as sensitive and kept local (Principle IV) but are **not** encrypted or password-protected in this feature. Rationale: the live database and CV files are already stored unencrypted on the same machine, and a forgotten password would permanently lose a backup — directly defeating the "no data is lost" goal. (Encryption may be a later enhancement.)
- Q: When restoring a backup created by a different app/schema version, what should the system do (FR-017)? → A: **Migrate older, refuse newer** — a backup from an older app/schema version is brought forward to the current schema during restore (the append-only migration chain makes this deterministic — Principle IX); a backup from a **newer** version is refused with a clear message (no downgrade). Same-version backups restore directly.
- Q: How should backup/restore interact with in-flight scans, AI enrichment, and other backup/restore operations? → A: **Serialize; restore pauses writes** — only one backup or restore runs at a time; a backup runs concurrently with scans/enrichment but captures a consistent point-in-time snapshot; a restore pauses scanning and enrichment writes for its duration and resumes them afterward.
- Q: Should the spec set a measurable performance target for backup/restore? → A: **Bounded for typical size + progress** — a backup or restore of a typical single-user data set (up to ~10,000 offers plus a handful of CV files) completes within ~60 seconds and shows visible progress; there is no hard cap that rejects larger data sets.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create a complete backup on demand (Priority: P1)

As the single user of my job-search tracker, I want to click one button and get a complete, downloadable backup of everything the app holds — without changing or risking my current data — so my offers, statuses, settings, CVs, and AI results are safe against accidental loss, a bad migration, or a wiped machine.

**Why this priority**: This is the explicit, smallest, highest-value slice and a viable MVP on its own. Just being able to capture a complete snapshot on demand delivers the core protection the user asked for ("make sure no data is lost"), independent of whether restore is built yet. It directly satisfies constitution Principle IX ("Your Data Is Recoverable").

**Independent Test**: With data present, click "Backup" in the UI and confirm a single archive downloads; verify the live data is unchanged afterward and the app stayed usable throughout; open the archive's manifest and confirm it lists every data category and the included CV files with counts.

**Acceptance Scenarios**:

1. **Given** the app holds offers, statuses, settings, CV(s), and enrichment/fit results, **When** I trigger a backup from the UI, **Then** I receive a single self-contained archive containing all of that data plus every uploaded CV file, and the system confirms success with a timestamp and a summary of what was captured.
2. **Given** a backup is running, **When** it completes, **Then** my live data is exactly as it was before (nothing modified, deleted, or locked) and the app remained usable while the backup ran.
3. **Given** the backup cannot be produced completely (e.g. a read fails partway), **When** the operation ends, **Then** the system reports a clear failure and does **not** present a partial archive as if it were a complete backup.
4. **Given** a fresh install with no offers and no CVs, **When** I trigger a backup, **Then** it still succeeds and produces a valid (complete-but-empty) archive.

---

### User Story 2 - Restore from a backup on demand (Priority: P2)

As the single user, I want to restore the app's data from a backup archive I previously downloaded — initiated from the UI, with a clear warning and a safety net — so I can recover after data loss, a corrupted database, or moving to a new machine, and end up exactly where my backup left off.

**Why this priority**: Restore is the recovery half of the round trip and the user explicitly asked for it, but it builds on having backups (US1) and is the destructive, higher-risk operation. Backup alone already protects the data, so restore is P2: essential, but sequenced after the MVP.

**Independent Test**: Take a backup, change or delete some data, then restore from that backup via the UI (upload the archive, confirm the warning); verify the data afterward matches the backup exactly (record counts per category and CV files match 1:1), and verify a safety backup of the pre-restore state was produced.

**Acceptance Scenarios**:

1. **Given** a previously created backup archive, **When** I start a restore from the UI and supply that archive, **Then** the system validates it for completeness/integrity *before* touching any live data, automatically creates and provides a safety backup of the current state, asks me to explicitly confirm that current data will be replaced, and only then replaces the data.
2. **Given** a confirmed restore completes, **When** I inspect the app, **Then** every data category and every uploaded CV file matches the supplied backup exactly — restored counts equal backed-up counts, with no additions, omissions, or silent transformations.
3. **Given** I supply a corrupt, incomplete, or non-backup file, **When** I attempt to restore, **Then** the system refuses before altering anything and my current data remains fully intact.
4. **Given** a restore fails partway (e.g. interrupted), **When** the operation ends, **Then** the data is left intact or fully recoverable from the automatic safety backup — never in a partially-restored, corrupt state.
5. **Given** a fresh/empty instance (e.g. a new machine or after data loss), **When** I restore from a backup, **Then** the app comes up with exactly the backed-up data — the backup does not depend on anything that lived only in the original running instance.

---

### User Story 3 - Trust a backup before relying on it (Priority: P3)

As the single user, I want to see exactly what a backup contains and confirm it is valid — without performing a restore — so I can trust that "no data is lost" before I delete the original or rely on the archive in an emergency.

**Why this priority**: Confidence and verifiability. It hardens the "no data is lost" guarantee and reduces the risk of discovering a bad backup only at the worst possible moment. It is independent of restore actually running, so it can ship after the core round trip.

**Independent Test**: Supply a backup archive in the UI and view its manifest (timestamp, per-category record counts, included CV file count, source app/schema version) and an integrity check result (valid / corrupt / incomplete) — all without changing any live data.

**Acceptance Scenarios**:

1. **Given** a backup archive, **When** I ask the app to inspect it, **Then** I see when it was taken, what it contains (per-category counts and the included CV files), the app/schema version it came from, and whether it passes an integrity/completeness check — with no change to live data.
2. **Given** an archive that fails the integrity/completeness check, **When** I inspect it, **Then** the failure is reported clearly and the archive is flagged as not safe to restore.

### Edge Cases

- **Empty data set**: a backup of a fresh install (no offers, no CVs) still succeeds and yields a valid archive; restoring it produces an empty-but-consistent state.
- **Large data set / many CV files**: backup and restore may take time; the UI shows progress and a clear outcome, never appears to hang silently, and the downloaded archive is not truncated.
- **CV file referenced in the datastore but missing on disk (or an orphan file with no record)**: the backup records the discrepancy in its manifest and captures what exists rather than failing silently; a restore reproduces the same captured set and surfaces the discrepancy.
- **Backup taken while a scan or AI enrichment write is in progress**: the backup is internally consistent (a coherent point-in-time snapshot) and does not corrupt or block the in-flight operation.
- **Restore while the app is actively scanning/serving**: the restore pauses scanning and enrichment writes for its duration (per FR-020) so it is consistent and not interleaved with new writes, then resumes them.
- **Second backup/restore triggered while one is already running**: rejected/queued — operations are serialized so at most one runs at a time (FR-020); the user is told one is already in progress.
- **Insufficient disk space / I/O failure during backup, safety pre-backup, or restore**: the operation fails clearly and leaves existing data intact (restore is all-or-nothing).
- **User navigates away or cancels mid-operation**: no partial/corrupt state results; an interrupted restore is reversible from the automatic safety backup.
- **Restore from a backup taken on a different (older/newer) app/schema version**: the version is detected; an older backup is migrated forward to the current schema, a newer backup is refused with a clear message, and a same-version backup restores directly (FR-017) — it never silently produces a corrupt or mismatched state.
- **Wrong file uploaded for restore** (e.g. the existing offers `export.json`/`.csv`, or an unrelated file): rejected by validation with a clear message before any data is touched.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to trigger a complete backup on demand from the application UI with a single action.
- **FR-002**: A backup MUST capture **all** of the application's persistent data required to fully reconstruct its current state — every stored record across all categories (configured job sources, offers and their observations/versions/events history, scan runs, schedule configuration, role groups, CV records and metadata, application/matching settings, offer enrichment results, and offer fit results) **and** every uploaded CV file stored outside the primary datastore. No category of stored data may be omitted.
- **FR-003**: Creating a backup MUST be non-destructive: it MUST NOT modify, delete, lock the user out of, or otherwise alter live data; the existing data MUST remain exactly as it was before the backup, and the application MUST remain usable while a backup runs.
- **FR-004**: A backup MUST be produced as a single, self-contained artifact delivered to the user as a download through the browser.
- **FR-005**: Each backup MUST carry a manifest recording the moment it was taken, the source app/schema version, the per-category record counts, and the set of included CV files — sufficient to confirm its completeness without restoring it.
- **FR-006**: The system MUST confirm to the user when a backup completes successfully (with timestamp and a content summary) and MUST clearly report failure with a reason when a complete backup cannot be produced; it MUST NOT present a partial or truncated backup as if it were complete.
- **FR-007**: A backup artifact MUST be in a documented, self-describing format from which the full application state can be reconstructed, independent of any single running instance (Principle IX — the data is never trapped in one binary/app version).
- **FR-008**: Users MUST be able to restore the application's data on demand from the UI by supplying (uploading) a previously created backup artifact.
- **FR-009**: Before a restore overwrites any live data, the system MUST automatically create a safety backup of the current state and make it available to the user, so the pre-restore data is never lost.
- **FR-010**: A restore MUST require explicit user confirmation that acknowledges current data will be replaced, reflecting the destructive nature of the operation.
- **FR-011**: The system MUST validate a supplied backup artifact for integrity and completeness **before** altering any live data, and MUST refuse to restore from a corrupt, incomplete, or unrecognized artifact — failing safely with the current data intact.
- **FR-012**: A restore MUST be all-or-nothing: if it cannot complete, the system MUST leave the existing data intact, or fully recoverable from the automatic safety backup, rather than in a partially-restored or corrupt state.
- **FR-013**: After a successful restore, the application's data — all record categories **and** uploaded CV files — MUST match the contents of the supplied backup exactly: restored counts equal backed-up counts, with no additions, omissions, or silent transformations.
- **FR-014**: A backup MUST be restorable into a clean/empty instance (e.g. a new machine or after data loss), not only into the instance that produced it.
- **FR-015**: Backup and restore MUST run entirely locally and MUST NOT transmit any backed-up data — which includes personal data and CVs (Principle IV) — to any external service.
- **FR-016**: Backup and restore actions MUST report progress and outcome to the user (in progress / succeeded / failed) and MUST NOT appear to hang silently, even for large data sets.
- **FR-017**: The system MUST detect the app/schema version a supplied backup was created from. A backup from an **older** version MUST be brought forward to the current schema during restore (using the append-only migration chain); a backup from a **newer** version MUST be refused with a clear message (no downgrade); a same-version backup restores directly. In no case may a version mismatch silently produce a corrupt or mismatched state.
- **FR-018**: This capability MUST be additive: the existing offers/statuses export (JSON/CSV) MUST keep working unchanged and remains distinct from the complete backup/restore feature.
- **FR-019**: Backup artifacts MUST NOT be encrypted or password-protected in this feature; they MUST be treated as sensitive (they contain personal data and CVs — Principle IV), kept local, and never committed to the repository. (A forgotten password must never be able to render a backup unrecoverable.)
- **FR-020**: Backup and restore operations MUST be serialized — at most one backup or restore runs at a time. A backup MUST run concurrently with scans/enrichment while capturing a consistent point-in-time snapshot; a restore MUST pause scanning and enrichment writes for its duration and resume them afterward, so the restored state is not interleaved with new writes.

### Key Entities *(include if feature involves data)*

- **Backup artifact**: a single self-contained snapshot of the full application state at a point in time — the complete datastore contents (all record categories) plus all uploaded CV files, plus a manifest.
- **Backup manifest**: metadata describing a backup — timestamp taken, source app/schema version, per-category record counts, the set/count of included CV files, and an integrity marker used to validate completeness before a restore.
- **Restore operation**: a user-initiated, validated, confirmed, all-or-nothing replacement of current data with a backup's contents, preceded by an automatic safety backup.
- **Safety (pre-restore) backup**: an automatic backup of the current state captured immediately before a restore replaces it, so the pre-restore state stays recoverable.
- **Offers export (existing)**: the existing human-readable JSON/CSV export of offers + statuses — complementary to, distinct from, and unchanged by this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can produce a complete backup from the UI in a single action; 100% of stored data categories and 100% of uploaded CV files present at that moment are represented in the resulting archive (zero categories or files omitted).
- **SC-002**: Creating a backup leaves the live data unchanged — a comparison of the data immediately before and after a backup shows zero differences — and the application stays responsive throughout.
- **SC-003**: Restoring a freshly created backup into an empty instance reproduces the exact same data: record counts per category and the CV file set match the source 1:1, with zero discrepancies.
- **SC-004**: A full round trip (back up → alter/wipe → restore) results in zero data loss — every offer, status, CV file, setting, and enrichment/fit present before the backup is present and identical after the restore.
- **SC-005**: 100% of restore attempts from a corrupt, incomplete, or non-backup file are rejected before any live data is altered; live data remains intact in 100% of such cases.
- **SC-006**: A recoverable safety backup of the current state exists for 100% of restore operations, so an unwanted or failed restore can be reversed.
- **SC-007**: 0 bytes of backed-up data are transmitted to any external service during backup or restore (the operations are fully local).
- **SC-008**: A user can confirm what a given backup contains (timestamp, per-category counts, included files, validity) without restoring it.
- **SC-009**: A backup or restore of a typical single-user data set (up to ~10,000 offers plus a handful of CV files) completes within ~60 seconds and shows visible progress throughout; larger data sets are not rejected by a hard size cap.

## Assumptions

- **Two stores, one backup**: the app is local-first and single-user; its data lives in (a) a local relational datastore (PostgreSQL) and (b) uploaded CV files stored on disk outside that datastore (the `cv-data` location). A backup is "complete" only if it captures **both** — this is the basis for FR-002, FR-013, and SC-001/SC-003.
- **Stack retained**: React (Vite, TypeScript) front end + .NET 10 (ASP.NET Core) back end + PostgreSQL (EF Core, append-only migrations), layered `Domain → Application → Infrastructure → Web`. Backup/restore is a new local capability exposed via the API plus a UI control; no new external dependency or service is required.
- **"On demand"** means user-triggered from the UI; scheduled or automatic periodic backups are **out of scope** for this feature (a possible later enhancement).
- **Delivery**: backups are downloaded through the browser (per clarification); there is **no** server-side backup catalog/history in this feature. The user retains the downloaded archives, and a restore is initiated by uploading a previously downloaded archive.
- **Restore is in scope** (per clarification) and is destructive-by-nature; it is guarded by pre-validation, an automatic safety pre-backup, explicit confirmation, and all-or-nothing semantics. The automatic safety pre-backup (FR-009) is written to a local `backups/` directory (consistent with download-only delivery) and is "made available" by surfacing its path in the UI/response — not by a second browser download. In-app browsing/management of multiple historical backups is not required (the file system / browser downloads serve that role).
- **Cross-version restore** handling (apply migrations on restore vs. refuse) is resolved in the plan; the spec only requires that a version mismatch never silently corrupts data (FR-017).
- **Format**: the backup uses a documented, self-describing format (a complete logical snapshot of the datastore bundled with the CV files and a manifest) so data is never trapped in one app version (Principle IX). The exact archive/dump mechanism is a plan/implementation decision.
- **Sensitivity & privacy (Principle IV)**: backup artifacts contain personal data and CVs; they are treated as sensitive, stay local, and are never committed to the repository. The existing `.gitignore` posture for the live DB/exports/CVs extends to backup artifacts. They are **not** encrypted/password-protected in this feature (FR-019) — the live DB/CVs are already plaintext locally, and a forgotten password must never make a backup unrecoverable; archive encryption is a possible later enhancement.
- **Complementary to export**: the existing offers/statuses JSON/CSV export remains the human-readable partial export; this feature adds the complete, machine-restorable backup. The two are independent.
