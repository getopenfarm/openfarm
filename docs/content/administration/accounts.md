---
title: Users, groups, and sessions
description: Create farm accounts, grant permissions, and manage active browser sessions.
---

Administrators create accounts under **Users & groups**. There is no public self-registration or email password-recovery flow.

## Add a user

1. Choose **Add user**.
2. Enter the username, email, and temporary password. Passwords require at least eight characters.
3. Select the user's groups.
4. Save the account.

Ask the user to change the temporary password under **My profile → Security**.

## Grant access

Each user receives all permissions granted by their groups, plus permissions granted directly to that user. The permission editor offers **None, View, Edit, Full, Custom** for each area. Select sensitive permissions, such as remote printer control, explicitly.

- **Administrators** is protected and has every permission.
- **Operators** grants permissions for daily work, with limits on removal, administration, credentials, and remote control.
- **Viewers** grants read access to business information.

The API enforces permissions even when a button or navigation link is hidden. A disabled control can mean the account can view an area but cannot change it. Keep at least one administrator account enabled. Before removing an ordinary group, remove its members.

## AI assistants

Users connect AI assistants themselves, from **My profile → Connected apps** or with an access token: see [AI assistants](ai-assistants.md). Administrators can see and revoke a user's connected apps and tokens from that account's page.

## Sessions and passwords

**My profile → Sessions** lists active browser sessions and can sign out one or all devices. Administrators can revoke another user's sessions from that account's page.

Browser sessions expire fourteen days after sign-in. Refreshing an access token does not extend this period. If you change your own password, your current browser session stays active and other sessions end. If an administrator resets the password, all sessions end. Either type of password change revokes personal access tokens.

Disabling an account blocks and revokes its sessions and tokens. Enabling it again does not restore old credentials. See [API access](../development/api.md) for personal tokens.

## Forgotten administrator password

Another administrator can reset the password in **Users & groups**. The initial password settings apply only when OpenFarm creates the first account. They do not replace an existing password. Keep more than one trusted administrator account and test your installation backup.
