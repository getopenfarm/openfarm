---
title: Account provisioning with authentik
description: Create passwordless accounts at first sign-in, and set their groups from authentik.
---

OpenFarm can create an account when a new user signs in through OIDC. This is just-in-time provisioning (JIT).
OpenFarm can also set the account's groups from authentik groups or entitlements at every sign-in.

This guide lists only the settings that differ from the defaults.
For the rules behind each setting, see the [SSO reference](sso-reference.md#account-creation).

## Preparation

- Configure [single sign-on with authentik](single-sign-on.md) first.
- Decide who may sign in with the bindings on the OpenFarm application in authentik. JIT creates an account for every user that authentik lets through.
- Make sure that authentik sends `email_verified=true` for verified users. See [Configure verified email in authentik](#configure-verified-email-in-authentik).

## Enable JIT

1. Open **Settings → Authentication** as an administrator.
2. Turn on **Create accounts on first sign-in (JIT)**.
3. Keep **Require verified email** on.
4. Select the **Default group** for new accounts. The default is **Viewers**. Use an empty group if new accounts must have no permissions until an administrator grants them.
5. Select **Save**.

Existing local accounts do not use JIT. Each user [links their account](single-sign-on.md#link-your-administrator-account) instead.

### Configure verified email in authentik

:::note
Since authentik 2025.10, the default `email` mapping sends `email_verified=false`. JIT fails with `email_unverified` until you replace that mapping.
:::

1. Set up email verification in authentik. Follow authentik's [Verify email addresses guide](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/verify-email-address/).
   Store the confirmed address in the `email_verified_address` user attribute.
2. Go to **Customization** > **Property Mappings** and click **Create**. Select **Scope Mapping**.
   - **Scope name**: `email`
   - **Expression**:
     ```python
     email = request.user.email
     verified_address = request.user.attributes.get("email_verified_address")
     return {
         "email": email,
         "email_verified": bool(email and verified_address == email),
     }
     ```
3. Go to **Applications** > **Providers** and edit the OpenFarm provider.
4. Under **Advanced protocol settings** > **Scopes**, replace the default `email` mapping with the new mapping. Do not select both.

Set `email_verified_address` only after the user confirms the address. Clear it when the address changes.
Do not return `True` for every user unless another trusted process verifies every address.

## Map provider groups to OpenFarm groups

Optional. Without a mapping, every new account gets the default group, and administrators manage groups under **Users & Groups**.

1. In authentik, create a group for each OpenFarm role, for example `openfarm-operators`, and add the users.
2. In OpenFarm, open **Settings → Authentication**. Keep **Groups claim** as `groups`.
3. Under **Group mapping**, select **Add mapping**. Enter the authentik group name, and select the OpenFarm group. Names are case-sensitive.
4. Select **Save**.

The default `profile` scope mapping already sends the `groups` claim, so no extra scope is necessary.

:::caution Do not assign mapped groups by hand
OpenFarm manages the mapped groups and the default group. The next sign-in removes such a group unless authentik lists it.
:::

### Use entitlements instead of groups

Optional. Entitlements are authentik's per-application roles. Use them to keep OpenFarm roles out of your shared group list.

#### Create application entitlements

1. Go to **Applications** > **Applications** and click the OpenFarm application.
2. Open the **Application entitlements** tab and click **New Entitlement**.
3. Enter a name for each role, for example `operator`, and click **Create**.
4. Expand the entitlement and click **Bind existing group/user**. Select the group or user.

Bind on the entitlement itself. A binding on the application controls only who may sign in.

#### Add the entitlements scope to the provider

1. Go to **Applications** > **Providers** and edit the OpenFarm provider.
2. Under **Advanced protocol settings** > **Scopes**, add authentik's built-in application entitlements mapping (scope name `entitlements`) to **Selected Scopes**.
3. Click **Update**.

If your authentik version has no mapping with scope name `entitlements`, create one. Go to **Customization** > **Property Mappings**, click **Create**, and select **Scope Mapping**. Set **Scope name** to `entitlements` and use this expression:

```python
return {
    "entitlements": [e.name for e in request.user.app_entitlements(provider.application)],
}
```

#### Update the OpenFarm settings

1. Open **Settings → Authentication**.
2. Enter these values:
   - **Additional scopes**: `entitlements`. This tells authentik to send the entitlements.
   - **Groups claim**: `entitlements`. This tells OpenFarm where to read them.
3. Under **Group mapping**, add a row for each entitlement name. Names are case-sensitive.
4. Select **Save**.

Other providers work the same way: list the scope under **Additional scopes**, and set **Groups claim** to the claim that has the roles.

### Map a provider group to Administrators

Optional. A mapping row can target **Administrators**. Every user in that authentik group or entitlement then gets full access, including **Settings**.

:::caution Protect the administrator group
- Let only authentik administrators edit that group or entitlement. Anyone who can add a user to it can make that user an OpenFarm administrator.
- Keep a tested local recovery administrator. See [Recover access](sso-reference.md#recover-access).
- A removal reaches OpenFarm at the user's next sign-in, which can take up to 14 days. To end access at once, disable the account or revoke its sessions under **Users & Groups**.
:::

Add the mapping row as in [Map provider groups to OpenFarm groups](#map-provider-groups-to-openfarm-groups), and select **Administrators** as the OpenFarm group.

## Configuration verification

1. Sign in with a new test user. Check that OpenFarm creates the account with the default group, or with the mapped groups.
2. Remove the test user from a mapped group in authentik and sign in again. Check that OpenFarm removes the group.
3. Disable the test account in OpenFarm. Check that its browser session and personal tokens stop working.

If the user gets the wrong groups, see [Troubleshoot group mapping](sso-reference.md#troubleshoot-group-mapping).
If the sign-in fails, see [Troubleshoot a refused sign-in](sso-reference.md#troubleshoot-a-refused-sign-in).

## Resources

- [SSO reference](sso-reference.md)
- [Authentik application entitlements](https://docs.goauthentik.io/add-secure-apps/applications/manage_apps/#application-entitlements)
- [Authentik property mappings](https://docs.goauthentik.io/add-secure-apps/providers/property-mappings/)
- [Authentik verify email addresses](https://docs.goauthentik.io/add-secure-apps/providers/oauth2/verify-email-address/)
