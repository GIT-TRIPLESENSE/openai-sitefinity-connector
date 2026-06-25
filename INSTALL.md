# Installing the OpenAI Machine Translation Connector on Sitefinity 15.4

This guide walks through integrating `OpenAIMachineTranslation` into an existing on-premises Sitefinity CMS 15.4 installation.

## Prerequisites

| Requirement | Details |
|---|---|
| Sitefinity CMS | 15.4.8626 on-premises |
| .NET Framework | 4.8 |
| Visual Studio | 2022 recommended |
| Progress NuGet feed | Required for `Telerik.Sitefinity.*` packages |
| OpenAI API key | Required for the connector `apiKey` parameter |
| Outbound HTTPS | Sitefinity server must reach `https://api.openai.com` or your configured `apiUrl` |

## Step 1 - Configure the Progress NuGet Feed

Sitefinity packages are hosted on the Progress private NuGet feed. You must have an active Sitefinity license to access it.

1. In Visual Studio, open **Tools > NuGet Package Manager > Package Manager Settings > Package Sources**.
2. Add a new source:
   - Name: `Progress`
   - URL: `https://nuget.sitefinity.com/nuget/`
3. When prompted for credentials, use your Progress/Telerik account.

## Step 2 - Add the Connector Project

1. Open your Sitefinity web application solution in Visual Studio.
2. Add `OpenAIMachineTranslation.csproj` as an existing project.
3. Add a project reference from `SitefinityWebApp` to `OpenAIMachineTranslation`.
4. Restore NuGet packages for the solution.

From the connector folder you can also restore directly:

```powershell
nuget restore OpenAIMachineTranslation.sln
```

## Step 3 - Build

1. Set the build configuration to `Release`.
2. Build the full Sitefinity solution.
3. Confirm the connector DLL exists at:

```text
OpenAIMachineTranslation\bin\Release\OpenAIMachineTranslation.dll
```

When the web app references the project, the DLL is copied to the web app `bin` folder during build. If deploying manually, copy:

- `OpenAIMachineTranslation.dll`
- `Newtonsoft.Json.dll` if the Sitefinity web app does not already include a compatible version

## Step 4 - Add the Leapmotor Glossary

Create this folder in the Sitefinity web app:

```text
App_Data\OpenAITranslation\
```

Copy:

```text
App_Data\OpenAITranslation\glossary.sample.json
```

to:

```text
App_Data\OpenAITranslation\glossary.json
```

Edit `glossary.json` with the approved Leapmotor glossary, brand rules, model names, market-specific wording, legal phrases, and terms that must remain untranslated.

The connector automatically includes the glossary content in the prompt. The local cache key includes the glossary hash, so updating the glossary automatically bypasses stale cached translations.

## Step 5 - Configure the Connector in Sitefinity

1. Log in to the Sitefinity backend as an Administrator.
2. Navigate to **Administration > Settings > Advanced > Translations > Connectors**.
3. Locate `OpenAIMachineTranslation`.
4. Expand **Parameters** and add these values:

| Key | Value |
|---|---|
| `apiKey` | OpenAI API key |
| `model` | Optional. Defaults to `gpt-5.4-mini` |
| `apiUrl` | Optional. Defaults to `https://api.openai.com/v1/responses` |
| `glossaryPath` | Optional. Defaults to `~/App_Data/OpenAITranslation/glossary.json` |
| `cachePath` | Optional. Defaults to `~/App_Data/OpenAITranslation/cache.json` |
| `timeoutSeconds` | Optional. Defaults to `30` |
| `maxRetries` | Optional. Defaults to `2` |
| `enableCache` | Optional. Defaults to `true` |

5. Set **Enabled** to `true`.
6. Save changes.
7. Restart the IIS application pool.

## Step 6 - Configure Languages

Sitefinity may pass cultures such as `fr-BE`, `de-CH`, or `en-AU`. The connector normalizes case and underscores, but keeps regional intent. Configure Sitefinity culture mappings only if your CMS emits a culture code that is not the target locale you want OpenAI to receive.

Leapmotor target set:

```text
it-it, en-au, de-at, nl-be, fr-be, fr-fr, de-de, fr-gp, en-ie,
fr-lu, en-mt, fr-mq, fr-mu, fr-ma, nl-nl, en-nz, pl-pl, pt-pt,
fr-re, sk-sk, en-za, es-es, fr-ch, it-ch, de-ch, en-gb, hr-hr,
cs-cz, el-gr, hu-hu, is-is, ro-ro, sl-si
```

## Step 7 - Smoke Test

Translate a small page that includes:

- A CTA such as `Book a test drive`
- Vehicle terms such as `range`, `charging`, and `electric vehicle`
- HTML tags such as `<strong>Leapmotor</strong>`
- A URL
- A placeholder such as `{0}` or `{{vehicleName}}`
- One regional English target such as `en-au` or `en-gb`
- One regional French/German target such as `fr-be` or `de-ch`

Confirm:

- Markup and placeholders are unchanged.
- The connector returns one translated string for each source string.
- `App_Data\OpenAITranslation\cache.json` is created after the first successful uncached translation.
- Repeating the same translation reuses the cache and avoids another OpenAI call.

## Troubleshooting

**Connector not visible**

- Ensure `OpenAIMachineTranslation.dll` is in the Sitefinity web app `bin` folder.
- Restart the IIS application pool.
- Check `App_Data\Sitefinity\Logs\` for assembly load errors.

**No API key configured**

- Verify the `apiKey` parameter is saved under the `OpenAIMachineTranslation` connector.

**401 or 403 from OpenAI**

- Verify the API key, project access, and model access.

**429 or 5xx from OpenAI**

- The connector retries transient failures using exponential backoff. If the error persists, check OpenAI rate limits and service status.

**Translations fail after glossary changes**

- Validate `glossary.json` as JSON.
- Confirm the IIS process can read `App_Data\OpenAITranslation\glossary.json`.

**Cache file not created**

- Confirm `enableCache` is `true`.
- Confirm the IIS process can write to `App_Data\OpenAITranslation\`.

**Protected token error**

- The model returned a translation that changed a masked HTML tag, URL, or placeholder. The connector retries and then fails rather than returning unsafe markup. If this happens often, reduce batch size in code or add a stricter glossary/style note for the affected content type.
