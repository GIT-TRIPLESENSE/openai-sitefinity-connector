using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Progress.Sitefinity.Translations;
using Telerik.Sitefinity.Translations;

[assembly: TranslationConnector(name: OpenAIMachineTranslationConnector.ConnectorName,
                                connectorType: typeof(OpenAIMachineTranslationConnector),
                                title: OpenAIMachineTranslationConnector.ConnectorTitle,
                                enabled: false,
                                parameters: new string[]
                                {
                                    OpenAIMachineTranslationConnector.ApiKey,
                                    OpenAIMachineTranslationConnector.Model,
                                    OpenAIMachineTranslationConnector.ApiUrl,
                                    OpenAIMachineTranslationConnector.GlossaryPath,
                                    OpenAIMachineTranslationConnector.PromptInstructions,
                                    OpenAIMachineTranslationConnector.AvoidRegionalLanguages,
                                    OpenAIMachineTranslationConnector.CachePath,
                                    OpenAIMachineTranslationConnector.TimeoutSeconds,
                                    OpenAIMachineTranslationConnector.MaxRetries,
                                    OpenAIMachineTranslationConnector.EnableCache
                                })]
namespace Progress.Sitefinity.Translations
{
    public class OpenAIMachineTranslationConnector : MachineTranslationConnector
    {
        protected override void InitializeConnector(NameValueCollection config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            this.apiKey = GetRequired(config, ApiKey, NoApiKeyExceptionMessage);
            this.model = GetOptional(config, Model, DefaultModel);
            this.apiUrl = GetOptional(config, ApiUrl, DefaultApiUrl);
            this.timeoutSeconds = GetOptionalInt(config, TimeoutSeconds, DefaultTimeoutSeconds, 1, 300);
            this.maxRetries = GetOptionalInt(config, MaxRetries, DefaultMaxRetries, 0, 5);
            this.enableCache = GetOptionalBool(config, EnableCache, true);
            this.glossaryPath = ResolveSitePath(GetOptional(config, GlossaryPath, DefaultGlossaryPath));
            this.promptInstructions = NormalizePromptInstructions(GetOptional(config, PromptInstructions, DefaultPromptInstructions));
            this.promptInstructionsHash = ComputeHash(this.promptInstructions);
            this.avoidRegionalLanguages = GetOptionalBool(config, AvoidRegionalLanguages, false);
            this.cachePath = ResolveSitePath(GetOptional(config, CachePath, DefaultCachePath));

            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;

            this.httpClient = new HttpClient();
            this.httpClient.Timeout = TimeSpan.FromSeconds(this.timeoutSeconds);

            this.LoadGlossary();
            this.LoadCache();
        }

        protected override List<string> Translate(List<string> input, ITranslationOptions translationOptions)
        {
            if (input == null)
            {
                return new List<string>();
            }

            if (translationOptions == null)
            {
                throw new ArgumentNullException("translationOptions");
            }

            var sourceLanguage = NormalizeLanguageCode(translationOptions.SourceLanguage);
            var targetLanguage = NormalizeLanguageCode(translationOptions.TargetLanguage);
            if (this.avoidRegionalLanguages)
            {
                sourceLanguage = GetMainLanguageCode(sourceLanguage);
                targetLanguage = GetMainLanguageCode(targetLanguage);
            }

            if (!string.IsNullOrEmpty(sourceLanguage) && string.Equals(sourceLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return new List<string>(input);
            }

            var output = new List<string>(input);
            var misses = new List<TranslationItem>();

            for (var i = 0; i < input.Count; i++)
            {
                var sourceText = input[i];
                if (string.IsNullOrWhiteSpace(sourceText))
                {
                    output[i] = sourceText;
                    continue;
                }

                var cacheKey = this.CreateCacheKey(sourceText, sourceLanguage, targetLanguage);
                string cachedText;
                if (this.TryGetCachedTranslation(cacheKey, out cachedText))
                {
                    output[i] = cachedText;
                    continue;
                }

                var masked = MaskProtectedText(sourceText);
                misses.Add(new TranslationItem
                {
                    Index = i,
                    OriginalText = sourceText,
                    MaskedText = masked.Text,
                    ProtectedValues = masked.ProtectedValues,
                    CacheKey = cacheKey
                });
            }

            if (misses.Count == 0)
            {
                return output;
            }

            try
            {
                var translated = this.TranslateMissesAsync(misses, sourceLanguage, targetLanguage).GetAwaiter().GetResult();
                foreach (var item in translated)
                {
                    output[item.Key] = item.Value;
                }
            }
            finally
            {
                this.SaveCache();
            }

            return output;
        }

        private async Task<Dictionary<int, string>> TranslateMissesAsync(List<TranslationItem> misses, string sourceLanguage, string targetLanguage)
        {
            var output = new Dictionary<int, string>();

            for (var start = 0; start < misses.Count; start += MaxItemsPerRequest)
            {
                var batch = misses.Skip(start).Take(MaxItemsPerRequest).ToList();
                var response = await this.TranslateBatchWithRetriesAsync(batch, sourceLanguage, targetLanguage).ConfigureAwait(false);

                foreach (var item in batch)
                {
                    string translatedText;
                    if (!response.TryGetValue(item.Index, out translatedText))
                    {
                        throw new InvalidOperationException("OpenAI response did not contain a translation for input index " + item.Index + ".");
                    }

                    ValidateProtectedValues(translatedText, item.ProtectedValues, item.Index);
                    translatedText = RestoreProtectedText(translatedText, item.ProtectedValues);
                    output[item.Index] = translatedText;
                    this.SetCachedTranslation(item.CacheKey, translatedText);
                }
            }

            return output;
        }

        private async Task<Dictionary<int, string>> TranslateBatchWithRetriesAsync(List<TranslationItem> batch, string sourceLanguage, string targetLanguage)
        {
            var output = new Dictionary<int, string>();
            var pending = batch;
            Exception lastError = null;

            for (var attempt = 0; attempt <= this.maxRetries; attempt++)
            {
                try
                {
                    var response = await this.TranslateBatchAsync(pending, sourceLanguage, targetLanguage).ConfigureAwait(false);
                    var validation = ValidateBatchResponse(response, pending);
                    foreach (var item in validation.ValidTranslations)
                    {
                        output[item.Key] = item.Value;
                    }

                    if (validation.FailedItems.Count == 0)
                    {
                        return output;
                    }

                    lastError = new OpenAIProviderOutputException(validation.ErrorMessage);
                    if (attempt >= this.maxRetries)
                    {
                        throw lastError;
                    }

                    pending = validation.FailedItems;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt >= this.maxRetries || !IsRetriable(ex))
                    {
                        throw;
                    }

                    await Task.Delay(GetRetryDelay(attempt)).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("OpenAI translation failed after retries.", lastError);
        }

        private static BatchValidationResult ValidateBatchResponse(Dictionary<int, string> response, List<TranslationItem> batch)
        {
            var result = new BatchValidationResult();
            foreach (var item in batch)
            {
                string translatedText;
                if (response == null || !response.TryGetValue(item.Index, out translatedText))
                {
                    result.FailedItems.Add(item);
                    result.Errors.Add("OpenAI response did not contain a translation for input index " + item.Index + ".");
                    continue;
                }

                try
                {
                    ValidateProtectedValues(translatedText, item.ProtectedValues, item.Index);
                    result.ValidTranslations[item.Index] = translatedText;
                }
                catch (OpenAIProviderOutputException ex)
                {
                    result.FailedItems.Add(item);
                    result.Errors.Add(ex.Message);
                }
            }

            return result;
        }

        private async Task<Dictionary<int, string>> TranslateBatchAsync(List<TranslationItem> batch, string sourceLanguage, string targetLanguage)
        {
            var request = this.BuildOpenAIRequest(batch, sourceLanguage, targetLanguage);
            var requestJson = request.ToString(Formatting.None);

            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, this.apiUrl))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.apiKey);
                httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using (var httpResponse = await this.httpClient.SendAsync(httpRequest).ConfigureAwait(false))
                {
                    var responseBody = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        throw new OpenAIRequestException(httpResponse.StatusCode, responseBody);
                    }

                    return ParseTranslationResponse(responseBody);
                }
            }
        }

        private JObject BuildOpenAIRequest(List<TranslationItem> batch, string sourceLanguage, string targetLanguage)
        {
            var items = new JArray();
            foreach (var item in batch)
            {
                items.Add(new JObject
                {
                    { "id", item.Index.ToString(CultureInfo.InvariantCulture) },
                    { "text", item.MaskedText },
                    { "protected_tokens", BuildProtectedTokensJson(item.ProtectedValues) }
                });
            }

            var userPayload = new JObject
            {
                { "source_language", sourceLanguage },
                { "target_language", targetLanguage },
                { "items", items }
            };

            return new JObject
            {
                { "model", this.model },
                { "store", false },
                { "prompt_cache_key", "sitefinity-openai-translation-" + this.glossaryHash.Substring(0, 12) + "-" + this.promptInstructionsHash.Substring(0, 12) + "-" + targetLanguage },
                { "input", new JArray
                    {
                        new JObject
                        {
                            { "role", "developer" },
                            { "content", this.BuildDeveloperInstructions() }
                        },
                        new JObject
                        {
                            { "role", "user" },
                            { "content", userPayload.ToString(Formatting.None) }
                        }
                    }
                },
                { "text", new JObject
                    {
                        { "format", BuildResponseFormat() }
                    }
                },
                { "max_output_tokens", EstimateMaxOutputTokens(batch) }
            };
        }

        private static JArray BuildProtectedTokensJson(Dictionary<string, string> protectedValues)
        {
            var tokens = new JArray();
            if (protectedValues == null)
            {
                return tokens;
            }

            foreach (var token in protectedValues.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                tokens.Add(token);
            }

            return tokens;
        }

        private string BuildDeveloperInstructions()
        {
            var builder = new StringBuilder();
            builder.AppendLine(this.promptInstructions);
            builder.AppendLine();
            builder.AppendLine("Connector requirements:");
            builder.AppendLine("Preserve all protected tokens that look like @@SFMT_*@@ exactly as written.");
            builder.AppendLine("Each input item includes protected_tokens. Every listed token must appear in that item's translated text exactly once, byte-for-byte, with no spaces or character changes inside the token.");
            builder.AppendLine("Preserve HTML tags, URLs, placeholders, whitespace intent, punctuation intent, model names, trim names, units, and legal wording.");
            builder.AppendLine("Return only JSON that matches the supplied schema, with one translation per input id.");
            builder.AppendLine("Leapmotor context and glossary JSON:");
            builder.AppendLine(this.glossaryJson);
            return builder.ToString();
        }

        private static JObject BuildResponseFormat()
        {
            return new JObject
            {
                { "type", "json_schema" },
                { "name", "sitefinity_translation_batch" },
                { "strict", true },
                { "schema", new JObject
                    {
                        { "type", "object" },
                        { "additionalProperties", false },
                        { "required", new JArray("translations") },
                        { "properties", new JObject
                            {
                                { "translations", new JObject
                                    {
                                        { "type", "array" },
                                        { "items", new JObject
                                            {
                                                { "type", "object" },
                                                { "additionalProperties", false },
                                                { "required", new JArray("id", "text") },
                                                { "properties", new JObject
                                                    {
                                                        { "id", new JObject { { "type", "string" } } },
                                                        { "text", new JObject { { "type", "string" } } }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static int EstimateMaxOutputTokens(List<TranslationItem> batch)
        {
            var totalChars = batch.Sum(x => x.MaskedText == null ? 0 : x.MaskedText.Length);
            var estimated = 512 + (totalChars / 2);
            return Math.Max(1024, Math.Min(8192, estimated));
        }

        private static Dictionary<int, string> ParseTranslationResponse(string responseBody)
        {
            var responseJson = JObject.Parse(responseBody);
            var responseText = ExtractResponseText(responseJson);
            if (string.IsNullOrWhiteSpace(responseText))
            {
                throw new InvalidOperationException("OpenAI response did not contain output text.");
            }

            var payload = JObject.Parse(StripCodeFence(responseText));
            var translations = payload["translations"] as JArray;
            if (translations == null)
            {
                throw new InvalidOperationException("OpenAI response JSON did not contain a translations array.");
            }

            var result = new Dictionary<int, string>();
            foreach (var translation in translations.OfType<JObject>())
            {
                var idValue = (string)translation["id"];
                var textValue = (string)translation["text"];
                int id;
                if (!int.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                {
                    throw new InvalidOperationException("OpenAI response contained an invalid translation id.");
                }

                result[id] = textValue ?? string.Empty;
            }

            return result;
        }

        private static string ExtractResponseText(JObject responseJson)
        {
            var directText = (string)responseJson["output_text"];
            if (!string.IsNullOrWhiteSpace(directText))
            {
                return directText;
            }

            var builder = new StringBuilder();
            var output = responseJson["output"] as JArray;
            if (output != null)
            {
                foreach (var outputItem in output.OfType<JObject>())
                {
                    var content = outputItem["content"] as JArray;
                    if (content == null)
                    {
                        continue;
                    }

                    foreach (var contentItem in content.OfType<JObject>())
                    {
                        var text = (string)contentItem["text"];
                        if (!string.IsNullOrEmpty(text))
                        {
                            builder.Append(text);
                        }
                    }
                }
            }

            if (builder.Length > 0)
            {
                return builder.ToString();
            }

            var choices = responseJson["choices"] as JArray;
            if (choices != null && choices.Count > 0)
            {
                var firstChoice = choices[0] as JObject;
                if (firstChoice != null)
                {
                    var message = firstChoice["message"] as JObject;
                    if (message != null)
                    {
                        return (string)message["content"];
                    }
                }
            }

            return null;
        }

        private static string StripCodeFence(string text)
        {
            var trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return trimmed;
            }

            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine < 0 || lastFence <= firstNewLine)
            {
                return trimmed;
            }

            return trimmed.Substring(firstNewLine + 1, lastFence - firstNewLine - 1).Trim();
        }

        private void LoadGlossary()
        {
            var content = DefaultGlossaryJson;
            if (!string.IsNullOrEmpty(this.glossaryPath) && File.Exists(this.glossaryPath))
            {
                content = File.ReadAllText(this.glossaryPath, Encoding.UTF8);
            }

            try
            {
                var glossary = JObject.Parse(content);
                if (string.IsNullOrWhiteSpace((string)glossary["version"]))
                {
                    glossary["version"] = DefaultPromptVersion;
                }

                this.promptVersion = (string)glossary["version"];
                this.glossaryJson = glossary.ToString(Formatting.None);
                this.glossaryHash = ComputeHash(this.glossaryJson);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("The OpenAI translation glossary JSON is invalid: " + this.glossaryPath, ex);
            }
        }

        private void LoadCache()
        {
            this.cache = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!this.enableCache || string.IsNullOrEmpty(this.cachePath) || !File.Exists(this.cachePath))
            {
                return;
            }

            try
            {
                var cacheJson = JObject.Parse(File.ReadAllText(this.cachePath, Encoding.UTF8));
                foreach (var property in cacheJson.Properties())
                {
                    this.cache[property.Name] = (string)property.Value ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                this.cache = new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private void SaveCache()
        {
            if (!this.enableCache || string.IsNullOrEmpty(this.cachePath) || !this.cacheDirty)
            {
                return;
            }

            lock (this.cacheLock)
            {
                var directory = Path.GetDirectoryName(this.cachePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var cacheJson = new JObject();
                foreach (var entry in this.cache.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    cacheJson[entry.Key] = entry.Value;
                }

                var tempPath = this.cachePath + ".tmp";
                File.WriteAllText(tempPath, cacheJson.ToString(Formatting.None), Encoding.UTF8);
                if (File.Exists(this.cachePath))
                {
                    File.Delete(this.cachePath);
                }

                File.Move(tempPath, this.cachePath);
                this.cacheDirty = false;
            }
        }

        private bool TryGetCachedTranslation(string cacheKey, out string translatedText)
        {
            translatedText = null;
            if (!this.enableCache)
            {
                return false;
            }

            lock (this.cacheLock)
            {
                return this.cache.TryGetValue(cacheKey, out translatedText);
            }
        }

        private void SetCachedTranslation(string cacheKey, string translatedText)
        {
            if (!this.enableCache)
            {
                return;
            }

            lock (this.cacheLock)
            {
                this.cache[cacheKey] = translatedText;
                this.cacheDirty = true;
            }
        }

        private string CreateCacheKey(string sourceText, string sourceLanguage, string targetLanguage)
        {
            var builder = new StringBuilder();
            builder.AppendLine(this.promptVersion);
            builder.AppendLine(this.promptInstructionsHash);
            builder.AppendLine(this.glossaryHash);
            builder.AppendLine(this.model);
            builder.AppendLine(sourceLanguage);
            builder.AppendLine(targetLanguage);
            builder.Append(sourceText);
            return ComputeHash(builder.ToString());
        }

        private static MaskedText MaskProtectedText(string text)
        {
            var protectedValues = new Dictionary<string, string>(StringComparer.Ordinal);
            var masked = ReplaceProtectedPattern(text, HtmlTagRegex, "HTML", protectedValues);
            masked = ReplaceProtectedPattern(masked, UrlRegex, "URL", protectedValues);
            masked = ReplaceProtectedPattern(masked, PlaceholderRegex, "PH", protectedValues);

            return new MaskedText
            {
                Text = masked,
                ProtectedValues = protectedValues
            };
        }

        private static string ReplaceProtectedPattern(string text, Regex regex, string label, Dictionary<string, string> protectedValues)
        {
            return regex.Replace(text, delegate(Match match)
            {
                var token = "@@SFMT_" + label + "_" + protectedValues.Count.ToString("0000", CultureInfo.InvariantCulture) + "@@";
                protectedValues[token] = match.Value;
                return token;
            });
        }

        private static void ValidateProtectedValues(string translatedText, Dictionary<string, string> protectedValues, int index)
        {
            foreach (var token in protectedValues.Keys)
            {
                var occurrenceCount = CountOccurrences(translatedText, token);
                if (occurrenceCount == 0)
                {
                    throw new OpenAIProviderOutputException("OpenAI response for input index " + index + " did not preserve protected token " + token + ".");
                }

                if (occurrenceCount > 1)
                {
                    throw new OpenAIProviderOutputException("OpenAI response for input index " + index + " repeated protected token " + token + ".");
                }
            }
        }

        private static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
            {
                return 0;
            }

            var count = 0;
            var startIndex = 0;
            while (true)
            {
                var foundIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal);
                if (foundIndex < 0)
                {
                    return count;
                }

                count++;
                startIndex = foundIndex + value.Length;
            }
        }

        private static string RestoreProtectedText(string text, Dictionary<string, string> protectedValues)
        {
            var result = text;
            foreach (var item in protectedValues.OrderByDescending(x => x.Key.Length))
            {
                result = result.Replace(item.Key, item.Value);
            }

            return result;
        }

        private static string NormalizeLanguageCode(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return string.Empty;
            }

            return languageCode.Trim().Replace('_', '-').ToLowerInvariant();
        }

        private static string GetMainLanguageCode(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                return languageCode;
            }

            var dashIndex = languageCode.IndexOf('-');
            return dashIndex > 0 ? languageCode.Substring(0, dashIndex) : languageCode;
        }

        private static string ResolveSitePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var value = path.Trim();
            if (value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
            {
                var context = HttpContext.Current;
                if (context != null)
                {
                    return context.Server.MapPath(value);
                }

                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value.Substring(2).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            }

            if (!Path.IsPathRooted(value))
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value);
            }

            return value;
        }

        private static string NormalizePromptInstructions(string promptInstructions)
        {
            if (string.IsNullOrWhiteSpace(promptInstructions))
            {
                return DefaultPromptInstructions.Trim();
            }

            return promptInstructions
                .Replace("\\r\\n", Environment.NewLine)
                .Replace("\\n", Environment.NewLine)
                .Trim();
        }

        private static string GetRequired(NameValueCollection config, string key, string errorMessage)
        {
            var value = config.Get(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(errorMessage);
            }

            return value.Trim();
        }

        private static string GetOptional(NameValueCollection config, string key, string defaultValue)
        {
            var value = config.Get(key);
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        }

        private static int GetOptionalInt(NameValueCollection config, string key, int defaultValue, int minValue, int maxValue)
        {
            var value = config.Get(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed < minValue || parsed > maxValue)
            {
                throw new ArgumentException("Invalid value for connector parameter '" + key + "'.");
            }

            return parsed;
        }

        private static bool GetOptionalBool(NameValueCollection config, string key, bool defaultValue)
        {
            var value = config.Get(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            bool parsed;
            if (!bool.TryParse(value, out parsed))
            {
                if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                throw new ArgumentException("Invalid value for connector parameter '" + key + "'.");
            }

            return parsed;
        }

        private static bool IsRetriable(Exception ex)
        {
            var requestException = ex as OpenAIRequestException;
            if (requestException == null)
            {
                return true;
            }

            var statusCode = (int)requestException.StatusCode;
            return statusCode == 408 || statusCode == 409 || statusCode == 429 || statusCode >= 500;
        }

        private static TimeSpan GetRetryDelay(int attempt)
        {
            var seconds = Math.Pow(2, attempt);
            return TimeSpan.FromSeconds(Math.Min(8, seconds));
        }

        private static string ComputeHash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        internal const string ConnectorName = "OpenAIMachineTranslation";
        internal const string ConnectorTitle = "OpenAI Machine Translation";
        internal const string ApiKey = "apiKey";
        internal const string Model = "model";
        internal const string ApiUrl = "apiUrl";
        internal const string GlossaryPath = "glossaryPath";
        internal const string PromptInstructions = "promptInstructions";
        internal const string AvoidRegionalLanguages = "avoidRegionalLanguages";
        internal const string CachePath = "cachePath";
        internal const string TimeoutSeconds = "timeoutSeconds";
        internal const string MaxRetries = "maxRetries";
        internal const string EnableCache = "enableCache";

        internal const string DefaultModel = "gpt-5.4-mini";
        internal const string DefaultApiUrl = "https://api.openai.com/v1/responses";
        internal const string DefaultGlossaryPath = "~/App_Data/OpenAITranslation/glossary.json";
        internal const string DefaultCachePath = "~/App_Data/OpenAITranslation/cache.json";
        internal const string NoApiKeyExceptionMessage = "No API key configured for OpenAI translations connector.";

        private const int DefaultTimeoutSeconds = 30;
        private const int DefaultMaxRetries = 2;
        private const int MaxItemsPerRequest = 20;
        private const string DefaultPromptVersion = "leapmotor-openai-translation-v2";
        private const string DefaultPromptInstructions = @"You are a professional automotive website translator for Leapmotor CMS content.
Translate short website fragments accurately and naturally for the requested target locale.
Use the Leapmotor context and glossary exactly where applicable.
When glossary entries define targets, use the target for target_language exactly; for regional target_language values, fall back to the base language target before translating freely.
For regional English targets, localize spelling and automotive terminology while keeping the text in English.
For single words or CTAs, prefer concise native marketing copy over literal word-by-word translation.";
        private const string DefaultGlossaryJson = @"{
  ""version"": ""leapmotor-openai-translation-v2"",
  ""brandContext"": ""Leapmotor is an electric vehicle brand. Translate concise CMS fragments for Leapmotor website pages, product pages, offers, forms, navigation, legal notices, and CTAs."",
  ""styleGuide"": [
    ""Keep Leapmotor as Leapmotor."",
    ""Keep model names, trim names, vehicle codes, measurements, charging units, warranty numbers, and legal references unchanged unless the locale convention requires punctuation or spacing changes."",
    ""Use clear automotive retail language. CTAs should sound native and concise."",
    ""Do not expand or invent claims that are not present in the source."",
    ""When a glossary entry has targets, use the requested target locale first, then fall back to the base language target.""
  ],
  ""terms"": [
    { ""source"": ""Leapmotor"", ""target"": ""Leapmotor"", ""note"": ""Brand name. Do not translate."" },
    { ""source"": ""EV"", ""note"": ""Electric vehicle. Use the common target-locale automotive term."" },
    { ""source"": ""range"", ""note"": ""Vehicle driving range, not a product selection range."" },
    { ""source"": ""charging"", ""note"": ""Vehicle battery charging context."" },
    { ""source"": ""test drive"", ""note"": ""Use the target-locale automotive retail expression."" }
  ],
  ""marketNotes"": {
    ""en-gb"": ""Use British English."",
    ""en-ie"": ""Use Irish/British English conventions."",
    ""en-au"": ""Use Australian English."",
    ""en-nz"": ""Use New Zealand English."",
    ""en-za"": ""Use South African English."",
    ""fr-be"": ""Use French suitable for Belgium."",
    ""fr-ch"": ""Use French suitable for Switzerland."",
    ""de-at"": ""Use German suitable for Austria."",
    ""de-ch"": ""Use German suitable for Switzerland."",
    ""it-ch"": ""Use Italian suitable for Switzerland."",
    ""nl-be"": ""Use Dutch suitable for Belgium.""
  }
}";

        private static readonly Regex HtmlTagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new Regex(@"\b(?:https?://|www\.)[^\s<>""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PlaceholderRegex = new Regex(@"(\{\{[^{}]+\}\}|\{[0-9A-Za-z_.:-]+\}|%\([A-Za-z0-9_]+\)[sdif]|%\d*\$?[sdif]|\$[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        private readonly object cacheLock = new object();
        private string apiKey;
        private string model;
        private string apiUrl;
        private string glossaryPath;
        private string cachePath;
        private string glossaryJson;
        private string glossaryHash;
        private string promptInstructions;
        private string promptInstructionsHash;
        private string promptVersion;
        private int timeoutSeconds;
        private int maxRetries;
        private bool enableCache;
        private bool avoidRegionalLanguages;
        private bool cacheDirty;
        private HttpClient httpClient;
        private Dictionary<string, string> cache;

        private sealed class TranslationItem
        {
            public int Index { get; set; }
            public string OriginalText { get; set; }
            public string MaskedText { get; set; }
            public string CacheKey { get; set; }
            public Dictionary<string, string> ProtectedValues { get; set; }
        }

        private sealed class MaskedText
        {
            public string Text { get; set; }
            public Dictionary<string, string> ProtectedValues { get; set; }
        }

        private sealed class BatchValidationResult
        {
            public BatchValidationResult()
            {
                this.ValidTranslations = new Dictionary<int, string>();
                this.FailedItems = new List<TranslationItem>();
                this.Errors = new List<string>();
            }

            public Dictionary<int, string> ValidTranslations { get; private set; }
            public List<TranslationItem> FailedItems { get; private set; }
            public List<string> Errors { get; private set; }

            public string ErrorMessage
            {
                get
                {
                    return string.Join(" ", this.Errors);
                }
            }
        }

        private sealed class OpenAIProviderOutputException : InvalidOperationException
        {
            public OpenAIProviderOutputException(string message)
                : base(message)
            {
            }
        }

        private sealed class OpenAIRequestException : Exception
        {
            public OpenAIRequestException(HttpStatusCode statusCode, string responseBody)
                : base("OpenAI request failed with HTTP " + (int)statusCode + " (" + statusCode + "): " + responseBody)
            {
                this.StatusCode = statusCode;
            }

            public HttpStatusCode StatusCode { get; private set; }
        }
    }
}
