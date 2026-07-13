# Progress.Sitefinity.Translations.OpenAIMachineTranslationConnector

> **Supported baseline**: Sitefinity CMS 15.4.8632.0, .NET Framework 4.8

This repository contains a custom Sitefinity machine translation connector that calls the OpenAI Responses API directly from the Sitefinity backend. It is intended for Leapmotor website translations where CMS text may arrive as very small fragments, including CTAs, labels, menu items, legal snippets, and single words.

The original Systran connector should remain a separate repository/package. This fork is now an independent OpenAI connector with its own assembly and connector identity:

- Assembly: `OpenAIMachineTranslation.dll`
- Connector name: `OpenAIMachineTranslation`
- Connector title: `OpenAI Machine Translation`

## How It Works

- Sitefinity calls `OpenAIMachineTranslationConnector.Translate(...)`.
- The connector masks HTML tags, URLs, and common placeholders before sending text to OpenAI.
- The request includes Leapmotor brand context and glossary JSON.
- The Responses API is asked for strict structured JSON output with one translation per source item.
- The connector restores protected values, preserves source order, and returns the translated strings to Sitefinity.
- A local persistent cache avoids repeated OpenAI calls for identical source/model/glossary/language combinations.

## Configuration

Configure the connector in Sitefinity under **Administration > Settings > Advanced > Translations > Connectors > OpenAIMachineTranslation**.

| Key | Required | Default | Description |
|---|---:|---|---|
| `apiKey` | Yes | | OpenAI API key. |
| `model` | No | `gpt-5.4-mini` | OpenAI model used for translation. |
| `apiUrl` | No | `https://api.openai.com/v1/responses` | Responses API endpoint. |
| `glossaryPath` | No | `~/App_Data/OpenAITranslation/glossary.json` | Leapmotor glossary/context JSON. |
| `promptInstructions` | No | Built-in Leapmotor translation prompt | Editable tone, style, and transcreation prompt. Use `\n` for line breaks if the CMS field is single-line. |
| `avoidRegionalLanguages` | No | `false` | When `true`, regional codes such as `fr-mq`, `fr-be`, `de-ch`, and `en-au` are translated as `fr`, `fr`, `de`, and `en`. |
| `cachePath` | No | `~/App_Data/OpenAITranslation/cache.json` | Persistent translation-memory cache. |
| `timeoutSeconds` | No | `30` | Per-request HTTP timeout. |
| `maxRetries` | No | `2` | Retries for transient failures, rate limits, and malformed provider output. |
| `enableCache` | No | `true` | Enables local persistent cache. |

The repository includes the Leapmotor EN-to-IT/FR/DE glossary at:

```text
App_Data\OpenAITranslation\glossary.json
```

Deploy that file to the same path in the Sitefinity web app, or set `glossaryPath` to another approved JSON glossary. `glossary.sample.json` remains available as a small template for new markets.

Glossary entries can contain per-language `targets`. For regional locales such as `fr-be`, `de-ch`, and `it-ch`, the connector keeps the full target locale in the prompt and the glossary instructs OpenAI to fall back to the base `fr`, `de`, or `it` target when no regional override exists.

The `promptInstructions` setting lets CMS administrators tune tone, brand guidance, and translation style without rebuilding the connector. The default follows Leapmotor's Simple & light, Sensorial & aspirational, and Warm & reassuring tone pillars and asks the model to transcreate wordplay instead of producing literal calques. The connector always appends fixed rules for JSON structure, protected tokens, placeholders, URLs, HTML, independent treatment of batch items, and conservative handling of genuinely ambiguous fragments. The complete developer instructions are hashed into both cache identities, so changing the configured prompt, fixed requirements, glossary, or market notes prevents reuse of translations produced under different guidance.

An explicitly configured `promptInstructions` value replaces the built-in default. Existing installations must clear that value to adopt the new default, or update the configured value with the same tone and transcreation guidance. The external glossary style guide is still appended in either case.

## Translation Behavior

Before translating each fragment, the default prompt asks the model to infer whether it is a label, CTA, headline, tagline, idiom, or wordplay. For creative copy it should preserve the intended meaning, tone, brevity, and rhetorical effect with a natural target-locale equivalent rather than matching the source word for word. It must not invent or strengthen product, performance, legal, or sustainability claims.

Items sent together in a batch are still treated as independent CMS fragments. The model must not transfer a product, model, grammatical gender, meaning, or creative device from one item to another. If an isolated fragment remains genuinely ambiguous, it uses the most conventional neutral automotive interpretation instead of manufacturing wordplay or unsupported meaning.

For languages with grammatical gender or noun classes, the glossary also makes vehicle agreement locale-aware: market-specific conventions take priority, otherwise agreement follows the explicit or implied vehicle-category noun. The Italian guidance treats standalone `T03` references as feminine and `SUV` as a masculine head noun, while explicit nouns still control agreement, for example `la T03` but `il modello T03`.

The connector keeps regional language intent instead of collapsing cultures to neutral language codes. Examples:

- `en-au`, `en-gb`, `en-nz`, `en-za`: localized English variants
- `fr-be`, `fr-fr`, `fr-ch`: regional French variants
- `de-at`, `de-de`, `de-ch`: regional German variants
- `it-it`, `it-ch`: regional Italian variants
- `nl-be`, `nl-nl`: regional Dutch variants

Set `avoidRegionalLanguages=true` when regional variants should intentionally reuse the main language translation. In that mode, `fr-mq`, `fr-be`, and `fr-fr` are sent to OpenAI and cached as `fr`; `de-at` and `de-ch` as `de`; and regional English targets as `en`.

Protected content is masked before the OpenAI call and restored afterwards:

- HTML tags such as `<strong>` and `</a>`
- URLs such as `https://www.leapmotor.com`
- Common placeholders such as `{0}`, `{vehicleName}`, `{{cta}}`, `%s`, and `$value`

If OpenAI does not preserve a protected token, the connector retries. If retries are exhausted, Sitefinity receives an exception instead of silently publishing broken markup or placeholders.

## Cost Estimate

Estimate basis:

- 500k English source characters per target language, based on the previous Microsoft Translator volume.
- About 125k source tokens per target language.
- `gpt-5.4-mini` pricing from OpenAI API pricing on June 25, 2026:
  - Input: `$0.75 / 1M tokens`
  - Cached input: `$0.075 / 1M tokens`
  - Output: `$4.50 / 1M tokens`

Estimated launch cost for all 33 target locales:

| Target group | Locales | Estimated cost per locale with prompt cache |
|---|---|---:|
| English regional | `en-au`, `en-ie`, `en-mt`, `en-nz`, `en-za`, `en-gb` | `$1.22` |
| Dutch | `nl-be`, `nl-nl` | `$1.25` |
| Italian/German/other EU | `it-it`, `it-ch`, `de-at`, `de-de`, `de-ch`, `pl-pl`, `pt-pt`, `sk-sk`, `es-es`, `hr-hr`, `cs-cz`, `hu-hu`, `is-is`, `ro-ro`, `sl-si` | `$1.28` |
| French | `fr-be`, `fr-fr`, `fr-gp`, `fr-lu`, `fr-mq`, `fr-mu`, `fr-ma`, `fr-re`, `fr-ch` | `$1.30` |
| Greek | `el-gr` | `$1.36` |

Estimated total:

- With prompt caching: about `$42`
- Without prompt caching: about `$209`

Actual costs should be recalibrated from OpenAI usage logs after the first production translation run because source fragmentation, glossary size, target-language expansion, and cache hit rate all affect token use.

## Build

1. Configure the Progress NuGet feed.
2. Restore NuGet packages.
3. Build `OpenAIMachineTranslation.sln` in Release mode.
4. Deploy `bin\Release\net48\OpenAIMachineTranslation.dll` and `Newtonsoft.Json.dll` if the Sitefinity web app does not already provide the same compatible version.

See `INSTALL.md` for the full integration guide.
