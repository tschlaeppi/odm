# Authentication

ODM supports local accounts, LDAP / Active Directory and OpenID Connect side by side. Local accounts always keep working
(including the bootstrap administrator as emergency access). Roles: `Admin`, `Operator`, `ReadOnly`.

## LDAP / Active Directory

Disabled by default (`Identity:Ldap:Enabled=false`).

```json
"Identity": {
  "Ldap": {
    "Enabled": true,
    "Host": "dc01.example.org",
    "Port": 636,
    "Security": "Ldaps",            // or "StartTls" (usually port 389)
    "BaseDn": "DC=example,DC=org",
    "BindDn": "CN=svc-odm,OU=Service,DC=example,DC=org",
    "BindPassword": "…",            // better: environment variable Identity__Ldap__BindPassword
    "UserFilter": "(sAMAccountName={0})",   // OpenLDAP: "(uid={0})"
    "AdminGroup": "CN=odm-admins,OU=Groups,DC=example,DC=org",
    "OperatorGroup": "CN=odm-operators,OU=Groups,DC=example,DC=org",
    "ReadOnlyGroup": "CN=odm-readers,OU=Groups,DC=example,DC=org"
  }
}
```

Sign-in flow:

1. ODM searches the user with the service account (`BindDn`; empty = anonymous search, only if the directory allows it) and
   the `UserFilter`. The entered name is escaped (no LDAP injection). No or several hits: rejected.
2. The password is verified by binding **as that user**. An empty password is never accepted.
3. The role comes from the groups (`memberOf`): `Admin` before `Operator` before `ReadOnly`. **A user in none of the three
   groups cannot sign in.** Nested groups are not resolved; the user must be a direct member.
4. If a **local** user with the same name exists, only the local account applies.

Notes:

- There is deliberately no connection without TLS. The server certificate is validated by the system; on Linux the libldap
  configuration applies (e.g. `TLS_CACERT` in `/etc/ldap/ldap.conf` for a private CA).
- The Docker image contains `libldap`. Compose variables: `ODM_LDAP_*`; other options via `Identity__Ldap__*`.
- Role and display name are re-read at every sign-in; a revoked group takes effect when the token expires (`Jwt:ExpiryMinutes`).
- Directory users do not appear in a user list; their ID is a stable hash of provider and user name.

## OpenID Connect (Entra ID, authentik, Keycloak …)

Single sign-on with one or more identity providers; each one gets a button on the sign-in page. Disabled without configuration.

```json
"Identity": {
  "Oidc": {
    "PublicBaseUrl": "https://odm.example.org",
    "Providers": [
      {
        "Id": "entra",
        "DisplayName": "Microsoft Entra ID",
        "Authority": "https://login.microsoftonline.com/<directory-id>/v2.0",
        "ClientId": "<application-id>",
        "ClientSecret": "…",           // better: environment variable
        "GroupsClaim": "roles",
        "AdminValues": [ "odm-admin" ],
        "OperatorValues": [ "odm-operator" ],
        "ReadOnlyValues": [ "odm-readonly" ]
      }
    ]
  }
}
```

As environment variables: `Identity__Oidc__PublicBaseUrl`, `Identity__Oidc__Providers__0__Id`, `…__0__Authority`,
`…__0__ClientId`, `…__0__ClientSecret`, `…__0__AdminValues__0`, …

**Redirect URI** at the provider, exactly: `<PublicBaseUrl>/api/auth/oidc/callback`. For `npm run dev` use
`PublicBaseUrl = http://localhost:5173` (HTTP is only allowed for localhost).

**Microsoft Entra ID**

1. *App registrations → New registration*; platform *Web*, redirect URI as above.
2. *Certificates & secrets → New client secret*; use the **value** as `ClientSecret`.
3. Create *App roles* (allowed member types: users/groups), e.g. `odm-admin`, `odm-operator`, `odm-readonly`.
4. In the *Enterprise application*: assign groups to the roles under *Users and groups*; set *Assignment required* to *Yes*.
5. `Authority` with the **directory (tenant) ID** (not `common` or `organizations`), `GroupsClaim = "roles"`. Group object IDs
   (`groups`) work too but are incomplete in tokens with roughly 200+ groups; app roles are more robust.

**authentik**

1. *Applications → Create* with an *OAuth2/OpenID* provider: client type *confidential*, redirect URI as above (*strict*),
   scopes `openid`, `profile`, `email`.
2. `Authority` is the application's *OpenID Configuration Issuer* (`…/application/o/<slug>/`); `ClientId` and
   `ClientSecret` come from the provider.
3. authentik delivers group names in the `groups` claim; list them under `AdminValues` etc.

How it works:

- Authorization code flow with **PKCE**, `state` and `nonce`; the server exchanges the code itself (back channel).
- The **ID token** is validated: signature (keys from discovery, asymmetric algorithms only), issuer, audience (`ClientId`),
  expiry and `nonce`. Key rotation at the provider is picked up automatically.
- The role comes from `GroupsClaim`: `Admin` before `Operator` before `ReadOnly`. **Without a matching value there is no sign-in.**
- The ODM user ID is a stable hash of provider and `sub`. The displayed name is `preferred_username`, else `email`, else `sub`.
- The sign-in state lives in a short-lived signed cookie (10 minutes, `HttpOnly`, `SameSite=Lax`), so no server-side store is
  needed and multiple instances work.
- The ODM token travels back in the URL fragment, is never sent to a server, and is removed from the address immediately.

Limits: sign-out ends the ODM session only, not the one at the provider. Multi-tenant issuers (Entra `common`) are not supported.
