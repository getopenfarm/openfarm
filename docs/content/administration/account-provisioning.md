---
title: Account provisioning with authentik
description: Create passwordless accounts at first sign-in or manage their lifecycle through SCIM.
---

OpenFarm can create accounts through OIDC just-in-time provisioning (JIT) or through its SCIM 2.0 API.
Both features are disabled by default. Configure [OIDC sign-in](single-sign-on.md) first.

JIT creates an account when a new user signs in. SCIM lets authentik create, update, disable, and restore accounts without a login.
They can work together: a JIT account appears in SCIM under the same stable external identity.
Neither feature matches or merges accounts by email.

## Choose an enrollment policy

| Policy | Configuration | Result |
| --- | --- | --- |
| Existing local accounts | Leave JIT and SCIM disabled | Administrators create local accounts. Users link OIDC explicitly. |
| Create at first sign-in | Enable JIT | Users allowed by authentik can create passwordless OpenFarm accounts at first login. |
| Provision before sign-in | Enable SCIM and leave JIT disabled | Authentik must create the account before the user can sign in. |
| Create at sign-in and manage centrally | Enable JIT and SCIM | First login can create an account. SCIM manages later changes and offboarding. |

Apply application-access policies in authentik before enabling JIT.
New accounts receive the configured default OpenFarm group. The default is **Viewers**.
Use an existing empty group if new accounts must have no operational permissions until group synchronization completes.
Protected groups, including **Administrators**, cannot be provisioning targets.

## Enable JIT

1. Open **Settings → Authentication** as an administrator.
2. Turn on **Create accounts on first sign-in (JIT)**.
3. Keep **Require verified email** on under **Advanced provisioning settings**.
4. Select the **Default group** for new accounts.
5. Select **Save**.

Changes apply immediately. No container restart is required.

The OIDC ID token must contain `sub`, `preferred_username`, and a valid `email`.
The default policy also requires `email_verified=true`.
The display name comes from the optional `name` claim.
Configure authentik's `profile` and `email` scope mappings to supply these claims in the ID token.
Since authentik 2025.10, the default email mapping returns `email_verified=false`.
JIT account creation fails until the provider supplies a verified address.
Existing linked accounts can still sign in.

OpenFarm rejects username and email collisions. An existing local account must use explicit linking instead.
JIT never re-enables a disabled or deleted account. Existing account details are not overwritten on subsequent logins.
Use SCIM to keep those details synchronized.

### Configure verified email in authentik

First configure an email-verification process.
Follow authentik's [Verify email addresses guide](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/verify-email-address/).
If you store the confirmed address in the `email_verified_address` user attribute, use this custom scope mapping:

```python
email = request.user.email
verified_address = request.user.attributes.get("email_verified_address")
return {
    "email": email,
    "email_verified": bool(email and verified_address == email),
}
```

1. Open **Customization → Property Mappings** and create a **Scope Mapping**.
2. Set **Scope name** to `email` and enter the expression above.
3. Open the OpenFarm OAuth2/OIDC provider.
4. Replace the default `email` mapping with the custom mapping. Do not select both mappings.
5. Confirm that the ID token contains the expected email and `email_verified=true` for a verified test user.

Set `email_verified_address` only after the user confirms that address.
Clear it when the address changes, including changes from administrators or synchronized sources.
Do not return `True` unconditionally unless another trusted process verifies every current address.

## Align SCIM and OIDC identities

For each user, the SCIM `externalId` must equal the OIDC `sub` exactly.
OpenFarm also binds the identity to the configured OIDC issuer.
An email address or a username must not be used as an identity-matching shortcut.

With authentik, use the OIDC subject mode **Based on the User's hashed ID** and the same `user.uid` value for SCIM `externalId`.
The current authentik SCIM client uses `user.uid` when no property mapping supplies an external ID.
If you use another subject mode or a custom property mapping, check that both values match before synchronizing real users.
Do not change subject mode for existing users without an identity migration plan.

## Enable SCIM

1. Open **Settings → Authentication**.
2. Turn on **Directory provisioning (SCIM)**.
3. Select **Generate credential**, then **Copy new credential**.
4. Add the required external-to-OpenFarm group mappings under **SCIM group mappings**.
5. Select **Save**. OpenFarm stores the credential encrypted and does not show it again.
6. Create an authentik SCIM provider with the **SCIM base URL** shown in OpenFarm.
7. Choose token authentication and enter the credential you copied.
8. Select user and group property mappings. Map one account email, `userName`, `active`, and the aligned `externalId`.
9. Attach the SCIM provider as a backchannel provider to the authentik application used for OpenFarm.
10. Bind the intended users or an access group to the authentik application. Without user bindings, authentik can synchronize all users.
11. Set group synchronization filters separately. Group filters do not restrict which users synchronize.
12. Exclude service accounts unless they need OpenFarm accounts.
13. Synchronize a test user and verify their OpenFarm account, group access, and sign-in.

Keep the provisioning credential private. It grants lifecycle access to provisioned accounts.
It is separate from the OIDC client credential, personal tokens, and browser sessions.
It cannot authenticate ordinary OpenFarm API requests.
Keep certificate verification enabled in authentik and route `/scim` to OpenFarm over HTTPS.

Enter your actual authentik group names in each mapping.
The mappings are case-insensitive and point to existing, unprotected OpenFarm groups.
Unmapped external groups are stored but grant no permissions.
Saved mapping changes apply immediately to OpenFarm permission checks.
Update the mappings in Settings before renaming or removing a local group used by provisioning.

## Understand permissions and lifecycle changes

Mapped SCIM groups add permissions to the account's default and explicitly assigned local groups.
Removing an external membership removes its mapped access on the next authorization check.
It does not remove independent local grants. Review those grants when restricting access.

SCIM can manage accounts created by SCIM or JIT for the configured provider.
Existing local accounts, including those explicitly linked to OIDC, are not automatically converted into managed accounts.
Local recovery administrators remain outside SCIM's scope.

Managed accounts have no local password. Local password reset is blocked even while OIDC is optional.
Their name, email, and username are read-only in OpenFarm.
Administrators can add local permissions or disable access in an emergency.
SCIM remains authoritative: a later explicit `active=true` update can re-enable the account.

| Central action | OpenFarm behavior |
| --- | --- |
| Set `active=false` | Disable login and revoke browser sessions, connected apps, and personal tokens in one transaction. Keep group assignments. |
| Set `active=true` | Allow a new login. Previously revoked credentials remain revoked. |
| Delete the SCIM user | Disable access, revoke credentials, and remove local grants and external memberships. Hide the SCIM resource but preserve identity and activity history. |
| Create the same external identity after deletion | Explicitly restore the same account with the default group. Old credentials and extra grants remain removed. |

Deleting a user does not allow JIT to recreate or restore that identity.
Only an explicit SCIM creation restores a deleted account.
Central changes take effect when authentik successfully sends the provisioning request, not when an administrator first changes authentik.
Monitor authentik synchronization failures during offboarding.
An authentik group-filter change does not remove previously synchronized groups or memberships.
Verify the resulting OpenFarm permissions when changing synchronization scope.

## Supported SCIM operations

The API supports core User and Group schemas, discovery, create/read/update/delete, PUT, and PATCH.
Users support `userName`, immutable `externalId`, `displayName`, `name`, `active`, and one account email.
Groups support `displayName`, immutable `externalId`, and direct user memberships.
Group PATCH supports adding members, removing all members, and `members[value eq "user-id"]` removal.

Equality filters support `id` and `externalId`, plus `userName` for users or `displayName` for groups.
Lists use one-based `startIndex` and `count`, with at most 200 results per page.
Top-level `attributes` and `excludedAttributes` selections are supported.
Updates accept optional `If-Match` headers with the returned ETag. A stale version returns 412.

Limits are 256 KiB per request, 100 PATCH operations per request, and 1,000 members per group.
Bulk requests, sorting, nested groups, arbitrary filter expressions, enterprise schema extensions, and password provisioning are not supported.
Use a dedicated SCIM credential and the capability-discovery endpoint when configuring another provisioning client.
Set **Requests per minute** in Settings to adjust the provisioning rate limit.

## Verify before rollout

Test a new JIT login, a SCIM-created user, profile updates, and mapped group removal.
Disable a user and check that their existing browser session and personal token stop working.
Re-enable the user and check that a fresh login works while old credentials remain invalid.
Keep a tested local recovery administrator before requiring OIDC.

## References

- [Authentik SCIM provider](https://docs.goauthentik.io/add-secure-apps/providers/scim/) explains identity matching, synchronization scope, and update behavior.
- [Create an authentik SCIM provider](https://docs.goauthentik.io/add-secure-apps/providers/scim/create-scim-provider/) explains token authentication and backchannel attachment.

For other providers, verify their claim mappings and provisioning operations against the supported operations above.
OpenFarm currently accepts one account email and requires an immutable `externalId` equal to the OIDC subject.
