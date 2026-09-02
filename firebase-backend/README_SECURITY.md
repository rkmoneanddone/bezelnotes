# Bezel Sticky Notes - Firebase Security Model

## Trust boundary

The Windows desktop application is an untrusted client.

It is allowed to:

- authenticate a user through Firebase Authentication
- hold a short-lived Firebase ID token
- call approved HTTPS Cloud Functions
- read only user-visible backend state explicitly allowed by Firestore rules

It is NOT allowed to:

- write entitlement state directly
- start/reset a trial directly
- grant premium access
- change subscription state
- create/alter payment records
- change prices
- perform admin actions
- hold service-account credentials
- hold payment-provider secrets
- hold webhook secrets
- hold server signing keys

## Server-authoritative collections

Cloud Functions/Admin SDK own writes to:

- users
- entitlements
- installations
- subscriptions
- payments
- config
- releases

Firestore rules deny direct client writes to these collections.

## Transactions

Trial bootstrap uses a Firestore transaction.

The transaction atomically:

1. verifies the authenticated Firebase user
2. reads/creates entitlement state
3. anchors trial to Firebase Auth creation time
4. registers/updates installation
5. returns authoritative entitlement state

The client cannot reset the trial by reinstalling the app.

## Secrets

Sensitive values must use Google Cloud Secret Manager through Firebase Functions secrets.

Examples:

- PAYMENT_PROVIDER_SECRET
- PAYMENT_WEBHOOK_SECRET
- ADMIN_SIGNING_SECRET
- future external service credentials

Never commit those values to Git and never place them in the WPF application.

## Payment

Payment is intentionally not implemented yet.

When added, payment execution and verification must run only in Cloud Functions.

The desktop app may only request checkout creation and display server-verified status.

A client-side "payment successful" flag must never grant entitlement.

## Public Firebase client configuration

The Firebase API key, project ID and Cloud Function base URL are public client identifiers.

They are not privileged secrets and must never be treated as authorization.

Security comes from:

- Firebase Authentication
- verified ID tokens
- Firestore Security Rules
- Cloud Functions/Admin SDK
- IAM
- restricted Firebase API keys
- App Check/custom desktop attestation when implemented
- rate limiting / quotas
- Secret Manager for real secrets
