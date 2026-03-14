# Microsoft Entra App Registration Setup for HERM-MAPPER-APP

This guide matches the current HERM-MAPPER-APP authentication code and configuration in:

- `src/HERM-MAPPER-APP/Program.cs`
- `src/HERM-MAPPER-APP/Configuration/AuthenticationConfiguration.cs`
- `src/HERM-MAPPER-APP/Services/AppAuthenticationService.cs`
- `src/HERM-MAPPER-APP/appsettings.json`

The application expects Microsoft Entra ID group claims in the `groups` claim and maps them to these application roles:

- `Administrator`
- `Contributor`
- `Viewer`

For Microsoft Entra ID, use group object IDs in `RoleGroupMappings`.

## 1. Prerequisites

You need:

- Azure CLI installed
- Permission to create app registrations in Microsoft Entra ID
- Permission to create security groups in Microsoft Entra ID
- Permission to add group owners and members

Sign in first:

```powershell
az login
az account show --query "{tenantId:tenantId, subscriptionId:id, name:name}" -o table
```

Optional: keep Azure CLI state inside the repo while testing commands locally.

```powershell
$env:AZURE_CONFIG_DIR = (Resolve-Path '.\.artifacts').Path
```

## 2. Pick the names and URLs

Set a few variables before creating anything.

For local development:

```powershell
$appName = "HERM-MAPPER-APP"
$appHost = "https://localhost:5001"
$signinPath = "/signin-oidc"
$signoutPath = "/signout-callback-oidc"
```

For Azure App Service, use your real hostname instead of `https://localhost:5001`, for example:

```powershell
$appHost = "https://my-herm-mapper-app.azurewebsites.net"
```

The full redirect URIs will be:

- Sign-in: `$appHost/signin-oidc`
- Sign-out callback: `$appHost/signout-callback-oidc`

## 3. Create the three Entra security groups

The app currently maps Entra groups to application roles, so create one security group per role.

```powershell
$adminGroupName = "HERM-MAPPER-Administrator"
$contributorGroupName = "HERM-MAPPER-Contributor"
$viewerGroupName = "HERM-MAPPER-Viewer"

$adminGroup = az ad group create `
  --display-name $adminGroupName `
  --mail-nickname "herm-mapper-administrator" `
  --description "HERM-MAPPER administrators" | ConvertFrom-Json

$contributorGroup = az ad group create `
  --display-name $contributorGroupName `
  --mail-nickname "herm-mapper-contributor" `
  --description "HERM-MAPPER contributors" | ConvertFrom-Json

$viewerGroup = az ad group create `
  --display-name $viewerGroupName `
  --mail-nickname "herm-mapper-viewer" `
  --description "HERM-MAPPER viewers" | ConvertFrom-Json

$adminGroup.id
$contributorGroup.id
$viewerGroup.id
```

Save those three group object IDs. They are what this app should use in `RoleGroupMappings`.

## 4. Add owners to the groups

Get the object ID for the person who should own the groups:

```powershell
$ownerObjectId = az ad signed-in-user show --query id -o tsv
```

Then add that user as owner for each group:

```powershell
az ad group owner add --group $adminGroup.id --owner-object-id $ownerObjectId
az ad group owner add --group $contributorGroup.id --owner-object-id $ownerObjectId
az ad group owner add --group $viewerGroup.id --owner-object-id $ownerObjectId
```

If you want to add a different owner, resolve that user first:

```powershell
$otherOwnerObjectId = az ad user show --id "person@contoso.com" --query id -o tsv
az ad group owner add --group $adminGroup.id --owner-object-id $otherOwnerObjectId
```

## 5. Add members to the groups

Resolve each user to an Entra object ID, then add them to the correct group.

Example:

```powershell
$aliceId = az ad user show --id "alice@contoso.com" --query id -o tsv
$bobId = az ad user show --id "bob@contoso.com" --query id -o tsv
$carolId = az ad user show --id "carol@contoso.com" --query id -o tsv

az ad group member add --group $adminGroup.id --member-id $aliceId
az ad group member add --group $contributorGroup.id --member-id $bobId
az ad group member add --group $viewerGroup.id --member-id $carolId
```

Recommended access model:

- `Administrator`: full admin access
- `Contributor`: create and edit products and services
- `Viewer`: read-only access

## 6. Create the app registration

Create a confidential web app registration for OpenID Connect sign-in.

```powershell
$tenantId = az account show --query tenantId -o tsv

$app = az ad app create `
  --display-name $appName `
  --sign-in-audience AzureADMyOrg `
  --web-home-page-url $appHost `
  --web-redirect-uris "$appHost$signinPath" "$appHost$signoutPath" | ConvertFrom-Json

$app.appId
$app.id
```

Important:

- `appId` is the client ID
- `id` is the Entra object ID for the app registration

For this app, `AzureADMyOrg` is the right audience unless you explicitly want cross-tenant sign-in.

## 7. Enable group claims on the app registration

HERM-MAPPER-APP reads Entra group membership from the `groups` claim. Turn that on:

```powershell
az ad app update --id $app.appId --set groupMembershipClaims=SecurityGroup
```

Why `SecurityGroup`:

- it keeps the token focused on security groups
- it matches the app's current `GroupClaimType` of `groups`
- it avoids unnecessary extra directory object types

## 8. Create the service principal

Create the enterprise application service principal for the app registration:

```powershell
$sp = az ad sp create --id $app.appId | ConvertFrom-Json
$sp.id
```

You usually need this so the app shows up correctly as an enterprise application in the tenant.

## 9. Create a client secret

HERM-MAPPER-APP is configured as a confidential web client. Create a secret for it:

```powershell
$secret = az ad app credential reset `
  --id $app.appId `
  --append `
  --display-name "HERM-MAPPER-AppService" `
  --years 1 | ConvertFrom-Json

$secret.password
```

Store the secret immediately in a secure place. The value returned here is the one you will use in app settings.

For production, a certificate is better than a client secret, but the current app config supports either an empty secret or a configured client secret. If you stay with secrets, rotate them regularly.

## 10. Configure the application settings

Set these values in App Service configuration or local environment variables.

### Minimum OpenID Connect settings

```powershell
$clientId = $app.appId
$clientSecret = $secret.password

$adminGroupId = $adminGroup.id
$contributorGroupId = $contributorGroup.id
$viewerGroupId = $viewerGroup.id
```

App Service or environment variable format:

```powershell
HERM_Security__Authentication__OpenIdConnect__Enabled=true
HERM_Security__Authentication__OpenIdConnect__DisplayName=Microsoft Entra ID
HERM_Security__Authentication__OpenIdConnect__Authority=https://login.microsoftonline.com/<tenant-id>/v2.0
HERM_Security__Authentication__OpenIdConnect__ClientId=<client-id>
HERM_Security__Authentication__OpenIdConnect__ClientSecret=<client-secret>
HERM_Security__Authentication__OpenIdConnect__CallbackPath=/signin-oidc
HERM_Security__Authentication__OpenIdConnect__SignedOutCallbackPath=/signout-callback-oidc
HERM_Security__Authentication__OpenIdConnect__RequireHttpsMetadata=true
HERM_Security__Authentication__OpenIdConnect__GetClaimsFromUserInfoEndpoint=false
HERM_Security__Authentication__OpenIdConnect__NameClaimType=name
HERM_Security__Authentication__OpenIdConnect__EmailClaimType=email
HERM_Security__Authentication__OpenIdConnect__GivenNameClaimType=given_name
HERM_Security__Authentication__OpenIdConnect__SurnameClaimType=family_name
HERM_Security__Authentication__OpenIdConnect__GroupClaimType=groups
HERM_Security__Authentication__OpenIdConnect__SubjectClaimType=sub
HERM_Security__Authentication__OpenIdConnect__Scopes__0=openid
HERM_Security__Authentication__OpenIdConnect__Scopes__1=profile
HERM_Security__Authentication__OpenIdConnect__Scopes__2=email
HERM_Security__Authentication__OpenIdConnect__RoleGroupMappings__Administrator__0=<admin-group-object-id>
HERM_Security__Authentication__OpenIdConnect__RoleGroupMappings__Contributor__0=<contributor-group-object-id>
HERM_Security__Authentication__OpenIdConnect__RoleGroupMappings__Viewer__0=<viewer-group-object-id>
```

Concrete example:

```powershell
HERM_Security__Authentication__OpenIdConnect__Authority=https://login.microsoftonline.com/$tenantId/v2.0
HERM_Security__Authentication__OpenIdConnect__ClientId=$clientId
HERM_Security__Authentication__OpenIdConnect__ClientSecret=$clientSecret
HERM_Security__Authentication__OpenIdConnect__RoleGroupMappings__Administrator__0=$adminGroupId
HERM_Security__Authentication__OpenIdConnect__RoleGroupMappings__Contributor__0=$contributorGroupId
HERM_Security__Authentication__OpenIdConnect__RoleGroupMappings__Viewer__0=$viewerGroupId
```

If you want both local and Entra login enabled during rollout, leave this on:

```powershell
HERM_Security__Authentication__Local__Enabled=true
```

If you want Entra-only sign-in later:

```powershell
HERM_Security__Authentication__Local__Enabled=false
```

## 11. Equivalent JSON configuration

If you prefer `appsettings` JSON, this is the matching shape:

```json
{
  "Security": {
    "Authentication": {
      "Local": {
        "Enabled": true
      },
      "OpenIdConnect": {
        "Enabled": true,
        "DisplayName": "Microsoft Entra ID",
        "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
        "ClientId": "<client-id>",
        "ClientSecret": "<client-secret>",
        "CallbackPath": "/signin-oidc",
        "SignedOutCallbackPath": "/signout-callback-oidc",
        "RequireHttpsMetadata": true,
        "GetClaimsFromUserInfoEndpoint": false,
        "NameClaimType": "name",
        "EmailClaimType": "email",
        "GivenNameClaimType": "given_name",
        "SurnameClaimType": "family_name",
        "GroupClaimType": "groups",
        "SubjectClaimType": "sub",
        "Scopes": [
          "openid",
          "profile",
          "email"
        ],
        "RoleGroupMappings": {
          "Administrator": [
            "<admin-group-object-id>"
          ],
          "Contributor": [
            "<contributor-group-object-id>"
          ],
          "Viewer": [
            "<viewer-group-object-id>"
          ]
        }
      }
    }
  }
}
```

## 12. What each role can do in this app

Based on the current authorization policies:

- `Administrator`: full access, including users, configuration, mappings, and change log
- `Contributor`: read access plus create/edit/delete products and services
- `Viewer`: read-only access to catalogue, dashboard, reference, and reports

## 13. Validate the setup

Check the app registration:

```powershell
az ad app show --id $app.appId --query "{appId:appId,displayName:displayName,groupMembershipClaims:groupMembershipClaims,web:web}" -o jsonc
```

Check the groups:

```powershell
az ad group show --group $adminGroup.id --query "{id:id,displayName:displayName}" -o jsonc
az ad group show --group $contributorGroup.id --query "{id:id,displayName:displayName}" -o jsonc
az ad group show --group $viewerGroup.id --query "{id:id,displayName:displayName}" -o jsonc
```

Check a user's membership:

```powershell
az ad group member check --group $viewerGroup.id --member-id $carolId
```

## 14. App Service example

If the app is already deployed to Azure App Service, push the settings with `az webapp config appsettings set`:

```powershell
$resourceGroup = "my-resource-group"
$webAppName = "my-herm-mapper-app"

az webapp config appsettings set `
  --resource-group $resourceGroup `
  --name $webAppName `
  --settings `
  HERM_Security__Authentication__OpenIdConnect__Enabled=true `
  HERM_Security__Authentication__OpenIdConnect__DisplayName="Microsoft Entra ID" `
  HERM_Security__Authentication__OpenIdConnect__Authority="https://login.microsoftonline.com/$tenantId/v2.0" `
  HERM_Security__Authentication__OpenIdConnect__ClientId="$clientId" `
  HERM_Security__Authentication__OpenIdConnect__ClientSecret="$clientSecret" `
  HERM_Security__Authentication__OpenIdConnect__CallbackPath="/signin-oidc" `
  HERM_Security__Authentication__OpenIdConnect__SignedOutCallbackPath="/signout-callback-oidc" `
  HERM_Security__Authentication__OpenIdConnect__GroupClaimType="groups" `
  HERM_Security__Authentication__OpenIdConnect__Scopes__0="openid" `
  HERM_Security__Authentication__OpenIdConnect__Scopes__1="profile" `
  HERM_Security__Authentication__OpenIdConnect__Scopes__2="email" `
  HERM_Security__Authentication__OpenIdConnect__RoleGroupMappings__Administrator__0="$adminGroupId" `
  HERM_Security__Authentication__OpenIdConnect__RoleGroupMappings__Contributor__0="$contributorGroupId" `
  HERM_Security__Authentication__OpenIdConnect__RoleGroupMappings__Viewer__0="$viewerGroupId"
```

## 15. Troubleshooting

### User can sign in but gets "does not map to any configured application role"

This means:

- the `groups` claim is missing, or
- the user is not in any mapped group, or
- the wrong group object IDs were configured

First checks:

- confirm `groupMembershipClaims=SecurityGroup` on the app registration
- confirm the user is a member of one of the three groups
- confirm `RoleGroupMappings` uses the group object IDs, not the display names

### No `groups` claim appears in the token

Turn on temporary diagnostics in the app:

```powershell
HERM_Security__Authentication__OpenIdConnect__EmitTokensAndClaimsToConsole=true
```

Then inspect the app logs after a sign-in attempt.

### Group overage

This app currently expects real `groups` claim values and maps them directly in `AppAuthenticationService`. If a user belongs to many groups, Entra can emit overage indicators instead of full group IDs in the token. In that case, HERM-MAPPER-APP will not be able to map roles from the token as-is.

To avoid that problem:

- keep access groups simple and targeted
- avoid assigning heavy directory-wide group memberships to app users
- if overage becomes common, extend the app to resolve groups from Microsoft Graph after sign-in

### Wrong redirect URI

For this app, the redirect URIs must match the ASP.NET Core OpenID Connect middleware paths:

- `/signin-oidc`
- `/signout-callback-oidc`

## 16. Recommended final values for this app

Use these defaults unless you have a specific reason to change them:

- Sign-in audience: `AzureADMyOrg`
- Callback path: `/signin-oidc`
- Signed-out callback path: `/signout-callback-oidc`
- Group claim type: `groups`
- Scopes: `openid`, `profile`, `email`
- Role mappings: Entra security group object IDs for `Administrator`, `Contributor`, `Viewer`

