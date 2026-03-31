# Installing the Systran Machine Translation Connector on Sitefinity 15.4 (On-Premises)

This guide walks through integrating the `SystranMachineTranslation` connector into an existing on-premises Sitefinity CMS 15.4 installation.

---

## Prerequisites

| Requirement | Details |
|---|---|
| Sitefinity CMS | 15.4.8626 (on-premises, IIS) |
| .NET Framework | 4.8 |
| Visual Studio | 2022 (any edition) |
| NuGet CLI / Package Manager | v6+ |
| Systran API key | Obtain from https://platform.systran.net/user/admin#/apiKeys |
| Systran .NET client library | https://github.com/SYSTRAN/translation-api-csharp-client |
| Progress NuGet feed configured | Required to restore `Telerik.Sitefinity.*` packages |

---

## Step 1 — Configure the Progress NuGet Feed

Sitefinity packages are hosted on the Progress private NuGet feed. You must have an active Sitefinity license to access it.

1. In Visual Studio, open **Tools → NuGet Package Manager → Package Manager Settings → Package Sources**.
2. Add a new source:
   - **Name:** `Progress`
   - **URL:** `https://nuget.sitefinity.com/nuget/`
3. When prompted for credentials, use your **Progress/Telerik account** email and password.

Alternatively, add the feed to your `NuGet.config`:

```xml
<configuration>
  <packageSources>
    <add key="Progress" value="https://nuget.sitefinity.com/nuget/" />
  </packageSources>
  <packageSourceCredentials>
    <Progress>
      <add key="Username" value="YOUR_TELERIK_EMAIL" />
      <add key="ClearTextPassword" value="YOUR_TELERIK_PASSWORD" />
    </Progress>
  </packageSourceCredentials>
</configuration>
```

---

## Step 2 — Get the Systran .NET Client Library

The connector depends on the `SystranClientTranslationApiLib` project, which is **not** available on NuGet and must be built from source.

1. Clone (or download) the Systran .NET client library repository:
   ```
   git clone https://github.com/SYSTRAN/translation-api-csharp-client.git
   ```
2. Place the cloned folder **at the same level** as this repository. The expected relative path from the `.csproj` is:
   ```
   ..\translation-api-csharp-client\SystranClientTranslationApiLib\SystranClientTranslationApiLib.csproj
   ```
   Example layout:
   ```
   C:\Projects\
   ├── SystranMTConnector\          ← this repository
   └── translation-api-csharp-client\
       └── SystranClientTranslationApiLib\
   ```

---

## Step 3 — Add the Connector to Your Sitefinity Solution

1. Open your Sitefinity web application solution (`.sln`) in Visual Studio 2022.
2. Right-click the solution node → **Add → Existing Project…**
3. Browse to `SystranMTConnector\SystranMachineTranslation.csproj` and add it.
4. Right-click the **SitefinityWebApp** project → **Add → Project Reference…**
5. Check **SystranMachineTranslation** and click **OK**.

---

## Step 4 — Restore NuGet Packages

In the **Package Manager Console** (with the Progress feed configured):

```powershell
Update-Package -Reinstall -ProjectName SystranMachineTranslation
```

Or via the CLI from the solution root:

```
nuget restore SystranMachineTranslation.sln
```

Verify that the following packages are restored under `SystranMTConnector\packages\`:

- `Telerik.Sitefinity.Core.15.4.8626`
- `Telerik.Sitefinity.Translations.15.4.8626`
- `Progress.Sitefinity.Renderer.15.4.8626.64`
- `Progress.Sitefinity.Web.UI.2025.4.1321.462`
- `Telerik.Licensing.1.6.36`
- `ServiceStack.*.10.0.4`

---

## Step 5 — Build the Solution

1. Set the build configuration to **Release**.
2. Build the entire solution (**Build → Build Solution** or `Ctrl+Shift+B`).
3. Confirm there are no build errors. The output DLL will be at:
   ```
   SystranMTConnector\bin\Release\SystranMachineTranslation.dll
   ```

When you build with `SitefinityWebApp` referencing the connector project, the DLL is automatically copied to the web app's `bin\` folder. If you are deploying the DLL separately (e.g. pre-compiled), copy the following files to the Sitefinity site's `bin\` folder:

- `SystranMachineTranslation.dll`
- `SystranClientTranslationApiLib.dll`
- `RestSharp.dll` (from `packages\RestSharp.105.2.3\lib\net46\`)

---

## Step 6 — Configure the Connector in Sitefinity Admin

1. Log in to the Sitefinity backend as an Administrator.
2. Navigate to **Administration → Settings → Advanced → Translations → Connectors**.
3. Locate **SystranMachineTranslation** in the list.
4. Expand the **Parameters** section and add the following keys:

   | Key | Value |
   |---|---|
   | `apiKey` | Your Systran API key |
   | `apiUrl` | *(optional)* Custom Systran endpoint. Defaults to `https://api-platform.systran.net` if left empty |

5. Set the **Enabled** field to `true`.
6. Click **Save changes**.

---

## Step 7 — Configure Culture Mappings (if needed)

SYSTRAN Pure Neural Server only accepts **two-letter ISO 639-1 language codes** (e.g. `en`, `fr`, `de`). Sitefinity culture names may include region suffixes (e.g. `en-US`, `fr-FR`).

To map a Sitefinity culture to a neutral language code:

1. Navigate to **Administration → Settings → Advanced → Culture mappings**.
2. Add a mapping for each regional culture used on your site, e.g.:
   - `en-US` → `en`
   - `fr-FR` → `fr`
   - `de-DE` → `de`

---

## Step 8 — Enable Multilingual Mode

The Translations module is only visible when the site runs in multilingual mode.

1. Navigate to **Administration → Settings → Languages**.
2. Ensure at least one additional language is added and activated.
3. The **Translations** menu item will appear under **Administration**.

---

## Troubleshooting

**Connector not visible in the connectors list**
- Ensure `SystranMachineTranslation.dll` is present in the Sitefinity site's `bin\` folder.
- Restart the IIS application pool after adding the DLL.
- Check the Sitefinity error log at `App_Data\Sitefinity\Logs\` for assembly load errors.

**`No API key configured` exception**
- Verify the `apiKey` parameter is saved and non-empty in Advanced Settings.

**Translation returns empty or errors**
- Confirm the `apiUrl` is reachable from the server (check firewall/proxy rules).
- Validate your API key at https://platform.systran.net.
- Check that the source and target language codes are valid 2-letter ISO codes (see Culture Mappings above).

**Build error: missing `SystranClientTranslationApiLib`**
- Ensure the `translation-api-csharp-client` folder is placed at the correct relative path (see Step 2).

**NuGet restore fails for Sitefinity packages**
- Confirm the Progress NuGet feed is configured and your credentials are valid.
- Ensure your Sitefinity license covers version 15.4.
