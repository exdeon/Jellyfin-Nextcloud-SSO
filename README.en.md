![Nextcloud OAuth2](Branding/Banner.png)

# Nextcloud OAuth2 for Jellyfin

[Русский](README.md) | **English**

SSO plugin for **Jellyfin 12.1+**: users sign in to Jellyfin with their Nextcloud account
over OAuth 2.0 (auth code flow + OIDC discovery), without a separate password.

|              |                                    |
| ------------ | ---------------------------------- |
| Package name | `Nextcloud OAuth2`                 |
| GUID         | `5c4de6ba220f4ace9dce72ba60c66861` |
| Category     | `Authentication`                   |
| Target ABI   | Jellyfin `12.1.0.0`                |

## How it works

1. The user opens the sign-in link `https://<jellyfin>/sso/OID/start/nextcloud`.
2. The plugin redirects them to `/apps/oauth2/authorize` in Nextcloud.
3. Nextcloud sends the user back to `https://<jellyfin>/sso/OID/redirect/nextcloud` with a code.
4. The code is exchanged for a token, user data is read via the OCS API
   (`/ocs/v2.php/cloud/user`), and a regular Jellyfin session is issued.

Mapping is done by the **stable Nextcloud UID** → Jellyfin user
(`UserLinks` table); the name is only used as a fallback.

## Installation

### Method 1 — via the catalog repository (recommended)

1. **Dashboard → Plugins → Repositories → “+”**:
   - Name: `Nextcloud OAuth2`
   - URL: `https://raw.githubusercontent.com/exdeon/Jellyfin-Nextcloud-SSO/main/manifest.json`
2. **Dashboard → Plugins → Catalog** → _Nextcloud OAuth2_ → **Install**.
3. Restart Jellyfin.

Updates are then fetched automatically (the built-in _Plugin Updates_ task
runs at server start and every 24 hours).

### Method 2 — manually (sideload)

1. Download `Nextcloud-OAuth2-<version>.zip` from the [releases](https://github.com/exdeon/Jellyfin-Nextcloud-SSO/releases) page.
2. Extract it into the `Plugins/Nextcloud OAuth2_<version>/` folder in the Jellyfin
   configuration directory (`Jellyfin.Plugin.NextcloudOAuth2.dll` and `meta.json`
   must end up there).
3. Restart Jellyfin.

> A plugin installed manually is not listed in the catalog: its page will show
> “An error occurred while retrieving plugin info from repository”.
> To avoid that (and to get updates), connect the repository from method 1 —
> only the URL is needed, no folder on the server has to be touched.

## Configuration

### 1. Nextcloud — register an OAuth2 client

Open the Nextcloud admin security settings
(**Settings → Administration → Security**, direct URL `/settings/admin/security`)
and add a new client:

| Field        | Value                                           |
| ------------ | ----------------------------------------------- |
| Name         | `Jellyfin`                                      |
| Redirect URI | `https://<jellyfin>/sso/OID/redirect/nextcloud` |

Nextcloud issues a **Client Identifier** and a **Client Secret** — keep them.

Notes:

- Nextcloud only supports confidential clients; the Secret is mandatory.
- Authorization endpoint: `https://<nextcloud>/apps/oauth2/authorize`
- Token endpoint: `https://<nextcloud>/apps/oauth2/api/v1/token`
  (if pretty URLs are disabled — add `/index.php`).
- The plugin sends `scope=openid profile email`; in current Nextcloud versions the
  scopes are effectively not applied and the token grants full access to the user account.
- To skip the confirmation screen on sign-in for a trusted application:
  `occ config:app:set oauth2 skipAuthPickerApplications --type array --value '["Jellyfin"]'`

### 2. Jellyfin — configure the plugin

**Dashboard → Plugins → _Nextcloud OAuth2_ → settings**:

| UI field                     | Purpose                                                                                                                 | Default |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------- | ------- |
| Nextcloud server URL         | Base address without a trailing slash                                                                                   | —       |
| Client ID                    | Client Identifier from Nextcloud                                                                                        | —       |
| Client Secret                | Secret from Nextcloud; empty = keep the current one                                                                     | —       |
| Public base URL              | External Jellyfin address, used in the redirect URI. Needed behind a reverse proxy without configured forwarded headers | empty   |
| Create users automatically   | Create a Jellyfin user on first sign-in                                                                                 | on      |
| Match existing users by name | Migration option for older plugin versions                                                                              | on      |
| Use PKCE (S256)              | Only if the Nextcloud client was registered with PKCE support                                                           | off     |

Save the settings and press **“Check connection”**. Below on the same page there is
a field with the sign-in link and a “Copy link” button.

### 3. Sign in

```
https://<jellyfin>/sso/OID/start/nextcloud
```

The `?returnUrl=/` parameter sets the page to return to after signing in
(default `#!/home`). You can make that link the default page or bookmark it for users.

### 4. Button on the login screen (optional)

Jellyfin renders arbitrary HTML/Markdown on the login page — handy for placing
an SSO button next to the regular sign-in form.

**Dashboard → Branding → “Login disclaimer”** (direct URL `/dashboard/branding`).
Paste:

```html
<div style="text-align: center; width: 100%;">
  <div style="margin-bottom: 0.5em; opacity: 0.7; font-size: 0.9em;">or</div>
  <a href="/sso/OID/start/nextcloud"><span>🔐 Sign in with Nextcloud</span></a>
</div>
```

Save — the block is rendered at the bottom of the login page.

Things to keep in mind:

- The field is rendered as Markdown with HTML allowed and is sanitized by DOMPurify.
  Wrap the markup in blank lines above and below, otherwise markdown-it may not
  recognize it as an HTML block.
- Do not use Jellyfin's built-in classes on the button (`raised button-submit`)
  or `is="emby-button"`: DOMPurify strips the `is` attribute, and the login page script
  additionally adds the `button-link` class to every link
  (`_theme.scss` gives it `color: primary`, `emby-button.scss` gives it
  `background: transparent`), which collapses the background and the text into a single
  color. Your own `.nc-sso-btn` rule is safe: `<style>` lands in the document after
  the `<link>` in `<head>` and wins on equal specificity.
- Jellyfin itself adds `target="_blank"` and `rel="noopener noreferrer"` to **every**
  link in this field, so the sign-in opens in a new tab — after a successful
  authorization, close the tab with the old form.
- The `href` is a relative path: the button works both behind a reverse proxy and
  with the “Public base URL” filled in.
- Save the plugin settings first; otherwise `/sso/OID/start/nextcloud` returns
  `400` with a “not configured” message. The text follows the browser’s
  Accept-Language: `Nextcloud OAuth2 is not configured` (en) /
  `Nextcloud OAuth2 не настроен` (ru).

## Security

- `ClientSecret` and the links are stored **in plain text** in `configuration.xml` —
  restrict access to the file or container.
- After migrating all users, turn off “Match existing users by name”: while the option
  is enabled, a sign-in with an unknown UID can take over an existing Jellyfin account
  with a matching name.
- The Nextcloud token grants full access to the account — keep it only on the Jellyfin server.

## Disclaimer

The project is almost entirely made by AI, but every line of code has been reviewed by a human.

## License

See [LICENSE](LICENSE).
