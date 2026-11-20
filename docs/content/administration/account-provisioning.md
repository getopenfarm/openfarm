---
title: Account provisioning with authentik
description: Create passwordless accounts at first sign-in with OIDC.
---

OpenFarm can create accounts through OIDC just-in-time provisioning (JIT).
JIT is disabled by default. Configure [OIDC sign-in](single-sign-on.md) first.
The **Account provisioning** section appears while **OIDC authentication** is on.

JIT creates an account when a new user signs in. It does not match or merge accounts by email.
Leave JIT disabled if administrators must create accounts before users can sign in.
Existing local accounts must use explicit OIDC linking.

Apply application-access policies in authentik before enabling JIT.
New accounts receive the configured default OpenFarm group. The default is **Viewers**.
To set groups from authentik instead, add a [group mapping](#map-provider-groups-to-openfarm-groups).
Use an existing empty group if new accounts must have no operational permissions until an administrator grants access.
The default group cannot be a protected group, such as **Administrators**. A group mapping can target it. See [Map a provider group to Administrators](#map-a-provider-group-to-administrators).

## Enable JIT

1. Open **Settings → Authentication** as an administrator.
2. Turn on **Create accounts on first sign-in (JIT)**.
3. Keep **Require verified email** on beneath the JIT switch.
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
JIT never re-enables a disabled account. Existing account details are not overwritten on subsequent logins.
Without a group mapping, administrators manage group access in OpenFarm.

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

### Troubleshoot a failed sign-in

A refused sign-in always shows the same message on the login page.
The server log has one `OidcSignInRejected` warning that names the cause. It never contains claim values.

| Log reason | Cause | Fix |
| --- | --- | --- |
| `email_unverified` | The ID token has `email_verified=false`. | [Configure verified email](#configure-verified-email-in-authentik). |
| `username_claim_missing`, `email_claim_missing_or_invalid` | The ID token has no `preferred_username`, or no valid `email`. | Select the `profile` and `email` scope mappings on the authentik provider. OpenFarm reads the ID token only, not the userinfo endpoint. |
| `claim_too_long` | A username, email, or name has more than 256 characters. | Shorten the value in authentik. |
| `account_conflict` | An OpenFarm account already uses this username, email, or identity. | Link the existing account in My profile › Security. JIT never merges accounts. |
| `Username, email or identity is invalid or already in use.` | The username has characters other than letters, digits, and `- . _ @ +`, or another account has the same email. | Change the authentik username or email, or link the existing account. |
| `The default group must be an existing, unprotected group.` | The default group does not exist or is protected. | Select an existing, unprotected default group in Settings → Authentication. |
| `Each mapped group must be an existing group.` | A mapped OpenFarm group was removed. | Select an existing group for each mapping. |
| `last_administrator_would_be_removed` | The provider no longer lists the user for **Administrators**, and no other enabled administrator exists. | Make another account an administrator, or add the user to the provider group again. |
| `not_linked_and_jit_disabled` | The identity is not linked and JIT is off. | Enable JIT, or link the account in My profile. |
| `account_disabled_or_removed` | The account is disabled, or was removed during sign-in. JIT does not re-enable it. | Re-enable the account under **Users & Groups**. |
| `provider_logout_receipt` | The provider ended this session before sign-in finished. | Sign in again. |
| `provider_disabled_or_settings_changed`, `identity_conflict` | Authentication settings changed, or two first sign-ins raced. | Try again. |
| `duplicate_claim_or_invalid_settings` | The ID token repeats a claim, or the saved settings are invalid. | Check the provider's claim mappings and the saved settings. |

## Map provider groups to OpenFarm groups

Use a group mapping to set OpenFarm groups from authentik groups. Without a mapping, only the default group applies, and administrators manage groups under **Users & Groups**.

1. In authentik, create a group for each OpenFarm role, for example `openfarm-operators`. Add the users.
2. Keep the `profile` scope mapping on the OpenFarm provider. It puts a `groups` claim with the user's group names in the ID token.
3. In **Settings → Authentication**, leave **Groups claim** as `groups`.
4. Under **Group mapping**, select **Add mapping**. Enter the authentik group name and pick the OpenFarm group. Names are case-sensitive.
5. Select **Save**.

OpenFarm reads one claim only, the one named in **Groups claim**. It never combines two claims, so a group and an entitlement with the same name cannot both grant access.
To use authentik entitlements instead of groups, see [Use entitlements instead of groups](#use-entitlements-instead-of-groups).

OpenFarm applies the mapping at every sign-in, to accounts it created through JIT:

- A user in a mapped authentik group gets the OpenFarm group. A user in several groups gets all of them.
- A user in no mapped group gets the default group.
- OpenFarm manages the groups in the mapping and the default group. Any other group you assign under **Users & Groups** stays.
- If you remove a mapping or point it at another group, OpenFarm removes the group it granted earlier at the user's next sign-in.
- A mapping can target **Administrators**. See [Map a provider group to Administrators](#map-a-provider-group-to-administrators).
- Accounts you linked by hand are never changed.
- If you remove every mapping, the user loses the groups the mapping granted and gets the default group. Groups are then managed under **Users & Groups** again.

Do not assign a mapped group or the default group by hand. The next sign-in removes it unless authentik lists it.

A change in authentik reaches OpenFarm at the user's next sign-in. A browser session slides for three days, up to 14 days from sign-in, so an access change can take that long.
To apply it sooner, revoke the user's sessions under **Users & Groups**, or disable the account.

If the ID token has no values in the Groups claim, the user gets the default group. OpenFarm removes the groups the mapping granted earlier.
The server log then has one entry. It never contains claim values:

- `OidcGroupsClaimMissing`: the ID token has no such claim. The entry names the claim OpenFarm read, the claim names the ID token contained, and the scopes OpenFarm requested.
- `OidcGroupsClaimEmpty`: the ID token has the claim, but the list is empty.

An entry also appears for a user who legitimately has no groups or entitlements, so it is not always an error.
Check the claim for a test user in authentik before rollout. Gate who may sign in with a policy or group binding on the OpenFarm application in authentik.

#### Read the group claim log entry

| Entry | Cause | Fix |
| --- | --- | --- |
| `OidcGroupsClaimMissing` | The provider did not send the claim. The scope is not requested, the scope mapping is not on the provider, the provider does not include claims in the ID token, the mapping expression failed, or **Groups claim** has the wrong name. | Add the scope under **Additional scopes**. Select the mapping on the provider. Turn on **Include claims in id_token**. Check the provider's events for a mapping error. Compare **Groups claim** with **Claims present**. |
| `OidcGroupsClaimEmpty` | The provider sent the claim, but lists nothing for this user. With entitlements: the user, and every group of the user, is bound to no entitlement of the OpenFarm application. | Add the user to a provider group. With entitlements, bind the user or one of their groups on the entitlement itself. A binding on the application does not count. |
| No entry, but the user has only the default group | The claim has values, and none equals a mapping row. | Compare the values with the mapping rows. Matching is exact and case-sensitive. A trailing space does not match. |

### Use entitlements instead of groups

Entitlements are authentik's per-application roles. Use them to keep OpenFarm roles out of your shared group list.
authentik returns the claims of a scope mapping only when the client requests the scope of that mapping, and the mapping is on the provider.
OpenFarm always requests `openid`, `profile`, and `email`. It requests any other scope that you list under **Additional scopes**.
OpenFarm refuses `offline_access`, because it keeps no provider tokens.

1. Open the OpenFarm application in authentik. On **Application entitlements**, create one entitlement per role, for example `operator`.
2. Bind a user or group to each entitlement. A binding on the application does not bind its entitlements.
3. Open the OpenFarm OAuth2/OIDC provider. Under **Advanced protocol settings → Scopes**, add authentik's built-in application entitlements mapping (scope name `entitlements`) to **Selected Scopes**.
   Keep the default `profile` and `email` mappings selected. Keep **Include claims in id_token** on. OpenFarm reads the ID token only.
   A provider does not always have this mapping selected. Without it, authentik ignores the `entitlements` scope.
4. In **Settings → Authentication**, set **Additional scopes** to `entitlements` and **Groups claim** to `entitlements`.
   The scope tells authentik what to send. The claim tells OpenFarm where to read it. They have the same name only for entitlements.
   Add a mapping row for each entitlement name. Names are case-sensitive.
5. Sign in again with a test user. If the user does not get the group, [read the log entry](#read-the-group-claim-log-entry).

If your authentik version has no mapping with scope name `entitlements`, create one. Open **Customization → Property Mappings** and create a **Scope Mapping**. Set **Scope name** to `entitlements`, then select it in step 3:

```python
return {
    "entitlements": [e.name for e in request.user.app_entitlements(provider.application)],
}
```

See authentik's [application entitlements documentation](https://docs.goauthentik.io/add-secure-apps/applications/manage_apps/#application-entitlements) and [property mappings documentation](https://docs.goauthentik.io/add-secure-apps/providers/property-mappings/).

Other providers work the same way. List the scope that returns the claim under **Additional scopes**. Set **Groups claim** to the top-level claim that lists the user's groups or roles, for example `roles`.
Some providers refuse an unknown scope. Add only scopes that the provider defines.
Nested claims, such as Keycloak's `realm_access.roles`, are not supported.
Identity claims such as `sub`, `email`, and `name` are refused, because a user can edit some of them at the provider.

### Map a provider group to Administrators

A mapping can target **Administrators**. Every user in that provider group then gets full access, including **Settings**.
The default group cannot be **Administrators**, because every new account gets it.

Before you map it:

- Let only provider administrators edit that provider group. OpenFarm trusts the Groups claim. A person who can add a user to the group can make that user an OpenFarm administrator.
- Keep a tested local [recovery administrator](single-sign-on.md). A local account does not depend on the provider.
- Plan for the delay. A removal reaches OpenFarm at the user's next sign-in, up to 14 days. To end administrator access at once, revoke the user's sessions or disable the account under **Users & Groups**.

OpenFarm never removes the last enabled administrator at sign-in. If a user would lose **Administrators** and no other enabled administrator exists, the sign-in fails.
The server log then has `last_administrator_would_be_removed`.
Each group change is in the activity log as `OidcGroupsSynced`.

## Manage access and offboarding

JIT accounts have no local password. Local password reset is blocked.
Their name, email, and username are read-only in OpenFarm.
Administrators manage groups and extra permissions under **Users & Groups**.
Provider group changes reach OpenFarm at the next sign-in, and only for groups in the [group mapping](#map-provider-groups-to-openfarm-groups).
Keep the provider's subject mode stable after users enroll.

Disable an account in OpenFarm to revoke its browser sessions, connected apps, and personal tokens.
Re-enabling the account permits a fresh login. Revoked credentials remain invalid.
Provider disablement is not polled. Back-channel logout ends OIDC browser sessions but does not revoke personal tokens or connected apps.

Removing an account in OpenFarm removes its identity link. If JIT stays enabled and the provider still permits access, a later sign-in can create a new account.
Disable the account to keep its identity reserved during offboarding.

## Verify before rollout

Test a new JIT login and check its default group, or its mapped groups if you set a mapping.
Confirm that existing users can link their accounts without creating duplicates.
Disable a user and check that their browser session and personal token stop working.
Keep a tested local recovery administrator before requiring OIDC.
