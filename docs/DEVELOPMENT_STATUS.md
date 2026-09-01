# StickyNotes â€” Development Status

_Last updated: 2026-09-01_

Status values:
- âœ… DONE
- ðŸŸ¡ PARTIAL
- â¬œ TODO
- ðŸ”µ LATER

## Stable checkpoint

Current known remote/local line at time of planning:

```text
8031295 Use hand cursor on note tabs
bf0a7c4 Remove fan fade flicker
4dd7f49 Stabilize fan opening and inactivity close
b020adc Keep one fixed add button below fan
f7f2f1a Keep add button fixed during previews
```

## Desktop Core

| Feature | Status | Notes |
|---|---|---|
| WPF / .NET 10 app | âœ… DONE | Current application foundation |
| SQLite | âœ… DONE | Primary local data |
| Note create primitive | âœ… DONE | Existing handler |
| Note edit | âœ… DONE | Existing editor |
| Autosave | âœ… DONE | ~250ms |
| Saving/Saved indicator | âœ… DONE | Existing |
| Delete | âœ… DONE | Soft delete |
| Note colors | âœ… DONE | Existing |
| Rest strip | âœ… DONE | Right edge |
| Fan tabs | âœ… DONE | Stable |
| Fan scroll | âœ… DONE | Hidden scrollbar |
| Preview dwell | âœ… DONE | Existing |
| Preview switch | âœ… DONE | Existing |
| Preview close | âœ… DONE | Stabilized |
| Fixed `+` | âœ… DONE | No preview movement |
| Fan flicker | âœ… DONE | Fixed |
| Hand cursor | âœ… DONE | Current HEAD |
| Load all notes | ðŸŸ¡ PARTIAL | `Take(8)` must be removed |
| 10/15/20 notes test | â¬œ TODO | After cap removal |
| Preview click â†’ editor | â¬œ TODO | Existing editor should be reused |
| `+` â†’ editor | â¬œ TODO | Create exists, open missing |
| Editor â†’ fan â†’ rest validation | â¬œ TODO | Test and polish |
| Ordering | ðŸŸ¡ PARTIAL | SortOrder exists |
| Drag/reorder | ðŸ”µ LATER | Decide after V1 core |

## Settings

| Feature | Status |
|---|---|
| Settings window | â¬œ TODO |
| Persist settings | â¬œ TODO |
| Default note color | â¬œ TODO |
| Edge selection | â¬œ TODO |
| Right edge | âœ… DONE |
| Left edge | â¬œ TODO |
| Top edge | â¬œ TODO |
| Bottom edge | â¬œ TODO |
| Start With Windows | â¬œ TODO |
| Account section | â¬œ TODO |
| Subscription section | â¬œ TODO |
| Cloud section | â¬œ TODO |
| Version/About | â¬œ TODO |

## Backend â€” Firebase

| Feature | Status |
|---|---|
| Firebase Spark architecture selected | âœ… DONE |
| Firebase project/config | â¬œ TODO |
| Firebase Auth | â¬œ TODO |
| User profiles | â¬œ TODO |
| Installations | â¬œ TODO |
| Subscription records | â¬œ TODO |
| Payment records | â¬œ TODO |
| Releases | â¬œ TODO |
| Remote config | â¬œ TODO |
| Admin backend | â¬œ TODO |

## Privacy / Data Model

| Requirement | Status |
|---|---|
| Local-first notes | âœ… DONE |
| Our backend must not store notes | âœ… DECISION LOCKED |
| User-owned cloud for notes | âœ… DECISION LOCKED |
| Google Drive target | â¬œ TODO |
| OneDrive target | â¬œ TODO |
| Local encryption design | â¬œ TODO |
| Cloud conflict handling | â¬œ TODO |
| Tombstones | â¬œ TODO |
| Multi-device restore | â¬œ TODO |

## Installation / Versions

| Feature | Status |
|---|---|
| App-generated installation ID | â¬œ TODO |
| First/last seen | â¬œ TODO |
| OS version | â¬œ TODO |
| App version reporting | â¬œ TODO |
| Semantic version infrastructure | â¬œ TODO |
| Release records | â¬œ TODO |
| Update check | â¬œ TODO |
| Minimum supported version | â¬œ TODO |
| Release notes | â¬œ TODO |
| Git release tags | â¬œ TODO |

## Admin

| Feature | Status |
|---|---|
| Dashboard | â¬œ TODO |
| Users | â¬œ TODO |
| Installations | â¬œ TODO |
| Subscriptions | â¬œ TODO |
| Payments | â¬œ TODO |
| Versions/Releases | â¬œ TODO |
| Remote Config | â¬œ TODO |
| Feature Flags | â¬œ TODO |
| Support diagnostics | â¬œ TODO |

## Payments / Subscription

| Feature | Status |
|---|---|
| Payment provider selection | â¬œ TODO |
| Checkout | â¬œ TODO |
| Server verification | â¬œ TODO |
| Webhook architecture | â¬œ TODO |
| Active entitlement | â¬œ TODO |
| Trial | â¬œ TODO |
| Grace | â¬œ TODO |
| Expired behavior | â¬œ TODO |
| Local entitlement cache | â¬œ TODO |
| No note deletion on expiry | âœ… DECISION LOCKED |
| Remote price config | â¬œ TODO |

## Next execution order

1. Remove `Take(8)`.
2. Test many notes.
3. Preview click â†’ editor.
4. `+` â†’ editor.
5. Validate state transitions.
6. Ordering.
7. Settings window.
8. Persist settings.
9. Four-edge placement.
10. Start With Windows.
11. Version infrastructure.
12. Firebase.
13. Auth + installations.
14. Admin.
15. Subscription/payment.
16. User-owned cloud sync.

