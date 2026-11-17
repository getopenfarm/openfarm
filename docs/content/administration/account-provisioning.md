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

Add these settings to the deployment environment, then recreate the OpenFarm container:

```dotenv
Auth__Oidc__Jit__Enabled=true
Auth__Oidc__Jit__RequireVerifiedEmail=true
Auth__Provisioning__DefaultGroup=Viewers
```

The OIDC ID token must contain `sub`, `preferred_username`, and a valid `email`.
The default policy also requires `email_verified=true`.
Configure authentik's `profile` and `email` scope mappings to supply these claims.
The display name comes from the optional `name` claim.

OpenFarm rejects username and email collisions. An existing local account must use explicit linking instead.
JIT never re-enables a disabled or deleted account. Existing account details are not overwritten on subsequent logins.
Use SCIM to keep those details synchronized.

## Align SCIM and OIDC identities

For each user, the SCIM `externalId` must equal the OIDC `sub` exactly.
OpenFarm also binds the identity to the configured OIDC issuer.
An email address or a username must not be used as an identity-matching shortcut.

With authentik, use the OIDC **Hashed User ID** subject mode and the same `user.uid` value for SCIM `externalId`.
The current authentik SCIM client uses `user.uid` when no property mapping supplies an external ID.
If you use another subject mode or a custom property mapping, check that both values match before synchronizing real users.
Do not change subject mode for existing users without an identity migration plan.

## Enable SCIM

1. Generate a separate provisioning credential:

   ```sh
   openssl rand -hex 32
   ```

2. Add the credential and configuration to the deployment environment:

   ```dotenv
   Auth__Scim__Enabled=true
   Auth__Scim__BearerToken=replace-with-generated-credential
   Auth__Scim__RequestsPerMinute=300
   Auth__Provisioning__DefaultGroup=Viewers
   Auth__Scim__GroupMappings__openfarm-viewers=Viewers
   Auth__Scim__GroupMappings__openfarm-operators=Operators
   ```

3. Recreate the OpenFarm container.
4. Create an authentik SCIM provider with the base URL `https://farm.example.com/scim/v2`.
5. Choose token authentication and enter the same provisioning credential.
6. Select the user and group property mappings. Map one account email, `userName`, `active`, and the aligned `externalId`.
7. Attach the SCIM provider as a backchannel provider to the authentik application used for OpenFarm.
8. Limit synchronization to the intended users and groups through authentik application policies and synchronization settings.
9. Synchronize a test user and verify their OpenFarm account, group access, and sign-in.

Keep the environment file private. The provisioning credential grants lifecycle access to provisioned accounts.
It is separate from the OIDC client credential, personal tokens, and browser sessions.
It cannot authenticate ordinary OpenFarm API requests.
Keep certificate verification enabled in authentik and route `/scim` to OpenFarm over HTTPS.

Replace `openfarm-viewers` and `openfarm-operators` with your actual authentik group names.
The mappings are case-insensitive and point to existing, unprotected OpenFarm groups.
Unmapped external groups are stored but grant no permissions.
Changing deployment mappings requires container recreation.
Update the configuration before renaming or removing a local group used by provisioning.

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
Use a dedicated SCIM token and the capability-discovery endpoint when configuring another provisioning client.

## Verify before rollout

Test a new JIT login, a SCIM-created user, profile updates, and mapped group removal.
Disable a user and check that their existing browser session and personal token stop working.
Re-enable the user and check that a fresh login works while old credentials remain invalid.
Keep a tested local recovery administrator before requiring OIDC.
