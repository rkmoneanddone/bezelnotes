# StickyNotes â€” Master Product Scope

_Last updated: 2026-09-01_

This document is the source of truth for StickyNotes product scope, architecture, privacy model, local/cloud storage, settings, payments, installations, admin, releases, subscriptions, and development order.

## 1. Product Goal

StickyNotes is a lightweight Windows desktop sticky-note utility that lives on a selected screen edge and stays fast, unobtrusive, local-first, and privacy-focused.

Core interaction:

```text
12px edge strip
    â†“ hover
Fan / title tabs
    â†“ deliberate dwell over note
Read-only preview
    â†“ click preview
Full editable note
```

Create flow:

```text
+ click
   â†“
Create note
   â†“
Open full editor immediately
```

Hover must never directly open the editable note.

---

## 2. Technology

### Desktop
- C#
- WPF
- .NET 10
- Windows desktop app

### Local database
- SQLite
- Primary working copy of all notes
- App must remain usable offline

### Our backend
- Firebase
- Start on Firebase Spark / free tier
- Target: zero recurring backend cost during early development and initial usage
- Firebase stores control/business data only
- Firebase must NOT store user note content

### User-owned cloud
User note backup/sync must use the user's own cloud account.

Initial targets:
- Google Drive
- OneDrive

Possible later:
- Dropbox
- WebDAV-compatible storage

---

## 3. Data Ownership and Privacy

### Local device stores
- note title
- note content
- note color
- note order
- created/updated timestamps
- soft-delete state
- settings
- entitlement cache
- sync metadata
- pending sync queue

### User's own cloud stores
- note backup/sync data
- optionally synchronized app settings
- preferably encrypted note payloads

### Our Firebase backend stores
- user/account identity
- subscription/license state
- verified payment records
- installations
- app versions/releases
- remote config
- entitlement state
- operational/admin metadata

### Our backend must NOT store
- note titles
- note bodies
- attachments
- note database backups
- private note content

Product privacy message:

> Your notes stay on your computer and, if you enable sync, in your own cloud storage.

---

## 4. Local Storage

SQLite remains the primary source of truth for desktop editing.

Database:

```text
%LOCALAPPDATA%\StickyNotes\stickynotes.db
```

Existing note model:
- Id
- Title
- Content
- Color
- SortOrder
- CreatedAt
- UpdatedAt
- IsDeleted

Cloud/backend failure must never block editing.

Suggested sync flow:

```text
Typing
  â†“
SQLite autosave (~250ms idle)
  â†“
Local sync queue
  â†“
Cloud sync after longer idle / note close / background cycle
```

Do not upload on every keystroke.

---

## 5. Rest State

Required:
- Approx. 12px edge strip/pill
- Tiny note-color dashes
- No titles
- Minimal visual footprint
- Right edge is current implementation

---

## 6. Fan State

Required:
- Opens by hover intent
- Vertical shingled tabs
- Approx. 46x116
- Rounded outside edge
- Screen edge remains flush
- Vertical title text
- Hand cursor
- Smooth bounded animation
- No flicker
- `+` fixed below fan
- Titles stay visible while previewing other notes

The edge-title font is fixed product UI and is not user-customizable.

---

## 7. Many Notes

Required:
- 10â€“15+ notes
- Should also remain usable with 20+ notes
- Fixed-height fan viewport
- Hidden scrollbar
- Wheel scrolling
- No artificial data loading limit
- Preview disabled while wheel movement is active
- Hover dwell resumes after scrolling stops
- `+` remains fixed outside the scrolling region

Known current issue:
- startup code currently uses `notes.Take(8)` and must be removed

---

## 8. Preview

Required:
- read-only
- linked to hovered tab
- approximately 235px wide
- pastel note color
- title + body content
- selected tab may hide while its preview is open
- other tabs remain visible
- switching tabs switches previews without fan collapse
- lower previews move upward when needed
- preview must not overlap/move the fixed `+`
- leaving the interaction closes preview reliably
- fan then returns to rest

Hover = preview only.

Click = edit.

---

## 9. Full Editor

Already designed/existing foundation:
- editable title
- editable content
- color selection
- Saving/Saved state
- local autosave
- delete
- close

Required final behavior:
- click preview â†’ full editor
- `+` â†’ create note â†’ full editor
- close full editor â†’ fan
- leaving fan afterwards â†’ rest
- Escape closes full editor/fan appropriately

---

## 10. Notes Features

Required:
- create
- edit
- autosave
- delete
- soft delete
- colors
- ordering
- sensible new-note position
- persistent order
- optionally manual drag/reorder after core stability

Later possible:
- pin/favorite
- archive
- search
- tags
- checklist formatting
- richer text

These later features are not required for initial V1 unless separately approved.

---

## 11. Settings Window

A dedicated Settings window is required.

### General
- Start StickyNotes with Windows
- launch/rest behavior

### Note Placement
Global placement:
- Top
- Right
- Bottom
- Left

One active edge at a time.

Placement must persist across restart.

### Notes
Initial:
- default note color

Later:
- transparency
- animation speed
- note width
- themes
- keyboard shortcuts
- import/export
- sync controls

### Account
- signed-in account
- sign in
- sign out
- manage account

### Subscription
- plan
- active/trial/grace/expired
- expiry/renewal date
- manage subscription

### Cloud
- Google Drive connection
- OneDrive connection
- last sync
- sync now
- disconnect
- restore status

### App
- current version
- update status
- release notes
- About
- Exit StickyNotes

---

## 12. Start With Windows

Required:
- opt-in/opt-out toggle
- setting persisted
- launch safely into rest state
- should survive app upgrades
- do not force startup

Implementation depends on final packaging approach.

---

## 13. Four-Edge Placement

Required:
- Right
- Left
- Top
- Bottom

Current:
- Right only

Rules:
- all notes share one global placement
- fan/preview/editor geometry adapts to edge
- preview opens inward toward screen content
- settings controls selected edge
- setting persists locally

Implement after current right-edge core is fully stable.

---

## 14. Authentication

Use Firebase Authentication for StickyNotes account identity.

Account identity is used for:
- subscription
- entitlement
- installation association
- payment ownership
- admin support

Cloud-storage identity is separate.

Example:
- StickyNotes account: user@example.com
- connected note cloud: different Google/Microsoft account if user chooses

---

## 15. User-Owned Cloud Sync

Our backend does not store user note content.

Desktop sync engine:

```text
SQLite
   â†•
Sync engine
   â†•
Google Drive / OneDrive
```

Possible cloud layout:

```text
StickyNotes/
    manifest.json
    notes/
        <note-id>.json
```

or a compact encrypted payload/database.

### Sync requirements
- UpdatedAt/version metadata
- tombstones for deleted notes
- conflict detection
- deterministic merge rules
- preserve local data when conflicts happen
- retry queue
- offline operation
- multi-device restore
- never silently destroy newest data

### Encryption
Preferred long-term design:
- encrypt locally
- upload encrypted payload
- decrypt locally
- our backend never receives note plaintext

---

## 16. Firebase Backend

Firebase is selected because the initial requirement is zero recurring backend cost.

Use Spark/free-tier compatible services wherever possible.

Suggested control-plane data:

```text
users/{uid}
installations/{installationId}
subscriptions/{uid}
payments/{paymentId}
releases/{version}
config/app
```

Do NOT create:

```text
users/{uid}/notes
notes/{noteId}
```

for user content.

---

## 17. Installation Tracking

Required for licensing, version support, and basic operational metrics.

Suggested fields:
- installationId
- userId nullable before login
- platform = Windows
- osVersion
- appVersion
- firstSeenAt
- lastSeenAt
- releaseChannel later
- optional user-friendly device name

Avoid:
- invasive hardware fingerprinting
- unnecessary serial numbers
- private hardware identifiers

Installation ID should be app-generated and persisted.

---

## 18. Admin

Admin functionality is required.

### Admin sections
- Dashboard
- Users
- Installations
- Subscriptions
- Payments
- Versions / Releases
- Remote Config
- Feature Flags
- Support / diagnostics

### Dashboard examples
- total users
- active subscriptions
- trial users
- grace users
- expired users
- total installations
- installations by app version
- outdated versions
- payment successes/failures
- latest release adoption

Admin must never display note content because note content is not stored by us.

---

## 19. Subscription and Entitlement

Possible states:
- active
- trial
- grace
- expired

Rules:
- subscription expiry never deletes notes
- local DB never gets deleted due to payment state
- users retain their data
- paid restrictions apply only to features explicitly defined as paid
- entitlement cached locally for offline use
- grace mechanism avoids sudden lockout due to temporary network/backend outage

Pricing and feature split remain configurable and are not hard-coded permanently.

---

## 20. Payment Architecture

Payment state must be server-verified.

Never trust client-side checkout success alone.

Required flow:

```text
StickyNotes
    â†“
Checkout
    â†“
Payment provider
    â†“
Server-side verification / webhook
    â†“
Verified payment record
    â†“
Entitlement update
    â†“
Desktop refresh
```

Payment provider should be chosen later based on:
- no monthly platform cost if possible
- India support
- international payment support
- transaction-only fees preferred
- reliable webhook/API
- straightforward refund/support records

Because Firebase Spark is the zero-cost target, webhook hosting must be designed carefully. If a Firebase feature requires billing activation, evaluate a free-compatible external webhook path before committing.

---

## 21. Remote Configuration

Operational values should be changeable without shipping a new desktop build.

Possible config:
- latestVersion
- minimumSupportedVersion
- forceUpdate
- updateUrl
- releaseNotes
- monthlyPrice
- annualPrice
- currency
- trialDays
- gracePeriodDays
- renewalReminderDays
- feature flags
- maintenance message
- support links

The app caches config and uses safe defaults offline.

---

## 22. Version and Release Management

Required:
- semantic application version
- visible version in Settings/About
- Git commit for each stable development step
- release tags for shipped builds
- backend release records
- latest version
- minimum supported version
- release notes
- update notification

Possible later channels:
- stable
- beta

Forced updates should be reserved for critical compatibility/security cases.

---

## 23. Analytics / Operational Metrics

Only minimal, privacy-respecting metrics.

Possible:
- installation count
- app version distribution
- last-seen timestamp
- subscription state counts
- payment state counts
- update adoption

Do not collect:
- note text
- note titles
- user typing
- private note metadata not needed for operation

---

## 24. Current Implementation Snapshot

### DONE / largely working
- WPF/.NET application
- SQLite persistence
- note create/save/delete primitives
- soft deletion
- right-edge rest strip
- fan tabs
- scrollable fan
- fixed `+`
- preview suppressed while scrolling
- hover dwell
- preview switching
- preview inactivity close
- lower/last preview positioning
- bounded fan opening
- fan flicker fix
- hand cursor
- full editor UI foundation
- title/body editing
- ~250ms local autosave
- Saving/Saved state
- note colors
- delete
- editor close to fan

### PARTIAL
- many-note handling: UI scroll exists but startup loads only first 8
- note ordering: SortOrder exists but final UX not complete
- full editor: exists but preview click not wired
- `+`: creates note but does not immediately open editor

### TODO
- remove `Take(8)`
- test 10/15/20 notes
- click preview â†’ editor
- `+` â†’ editor
- verify editor/fan/rest transitions
- ordering cleanup
- Settings window
- persisted settings
- four-edge placement
- Start With Windows
- app version infrastructure
- Firebase backend
- Firebase Auth
- installation registration
- Admin
- remote config
- subscription
- payment verification
- entitlement cache/grace
- Google Drive sync
- OneDrive sync
- client-side cloud encryption
- multi-device restore

---

## 25. Development Plan

### Phase 1 â€” Finish local desktop core
1. Remove `Take(8)`.
2. Verify 10/15/20 note behavior.
3. Preview click â†’ full editor.
4. `+` â†’ create â†’ editor.
5. Verify editor â†’ fan â†’ rest.
6. Finalize note ordering.

### Phase 2 â€” Settings and Windows integration
1. Settings window shell.
2. Persist application settings.
3. Four-edge placement.
4. Start With Windows.
5. Default note preferences.

### Phase 3 â€” Release/version foundation
1. Central version source.
2. Display version in Settings.
3. Release metadata.
4. Update-check foundation.
5. release tags/process.

### Phase 4 â€” Firebase control plane
1. Firebase configuration.
2. Auth.
3. users.
4. installation registration.
5. config/release records.
6. admin foundation.

### Phase 5 â€” Commercial system
1. subscription model.
2. entitlement model.
3. local cache.
4. trial/grace.
5. payment provider.
6. server-side verification.
7. admin commercial views.

### Phase 6 â€” User-owned cloud
1. cloud provider abstraction.
2. Google Drive.
3. OneDrive.
4. sync queue.
5. tombstones/conflicts.
6. encryption.
7. cross-device restore.

---

## 26. Engineering Rules

- Keep the app lightweight.
- Prefer targeted changes.
- One risky interaction change at a time.
- Preserve known-good fan/preview behavior.
- Build after changes.
- Commit stable checkpoints.
- Keep backups outside the project.
- Never place duplicate source backups inside WPF project folders.
- Local editing must work without backend/cloud.
- Never delete notes because subscription expired.
- Our backend must never store note content.
- Avoid unnecessary data collection.
- Keep remote pricing/config changeable.
- Maintain DEVELOPMENT_STATUS.md with every completed feature.

---

## 27. Source of Truth

This file defines the intended product.

`DEVELOPMENT_STATUS.md` defines current implementation progress.

The code on `main` defines what exists today.

When code and scope differ, update the status document instead of assuming completion.

