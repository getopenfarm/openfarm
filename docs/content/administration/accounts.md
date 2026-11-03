---
title: Users, groups, and sessions
description: Create farm accounts, grant permissions, and manage active browser sessions.
---

Administrators create accounts under **Users & groups**. There is no public self-registration or email password-recovery flow.

## Add a user

Choose **Add user**, enter the username, email, temporary password, and groups, then save. Passwords require at least eight characters. Ask the user to change the temporary password under **My profile → Security**.

## Grant access

Users receive the union of their groups' permissions plus any individual extra abilities. The permission editor offers **None, View, Edit, Full, Custom** by area. Sensitive actions, including remote printer control, require explicit selection.

- **Administrators** is protected and has every permission.
- **Operators** receives routine operational abilities with limits on removal, administration, credentials, and remote control.
- **Viewers** receives business read access.

The API enforces permissions even when a button or navigation link is hidden. Disabled controls can mean the account can view an area but cannot change it. At least one enabled administrator must remain; an ordinary group must be empty before removal.

## Sessions and passwords

**My profile → Sessions** lists active browser sessions and can sign out one or all devices. Administrators can revoke another user's sessions from that account's page.

Browser sessions expire fourteen days after sign-in; refreshing an access token does not extend the deadline. Changing your own password preserves the current browser session and revokes others. An administrator password reset revokes every session. Both password changes revoke personal access tokens.

Disabling an account blocks and revokes its sessions and tokens. Enabling it again does not restore old credentials. See [API access](../development/api.md) for personal tokens.

## Forgotten administrator password

Another administrator can reset it in **Users & groups**. Seed password settings only apply when creating the first account and do not overwrite an existing password. Keep more than one trusted administrator and a tested backup for installation-level recovery.
