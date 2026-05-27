using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using Progress.Sitefinity.Translations;
using Telerik.Sitefinity.Translations;

[assembly: TranslationConnector(name: SystranMachineTranslationConnector.ConnectorName,
                                connectorType: typeof(SystranMachineTranslationConnector),
                                title: SystranMachineTranslationConnector.ConnectorTitle,
                                enabled: false,
                                parameters: new string[] { SystranMachineTranslationConnector.ApiKey, SystranMachineTranslationConnector.ApiUrl })]
namespace Progress.Sitefinity.Translations
{
    public class SystranMachineTranslationConnector : MachineTranslationConnector
    {
        #region Initialization
        protected override void InitializeConnector(NameValueCollection config)
        {
            this.apiKey = config.Get(SystranMachineTranslationConnector.ApiKey);
            if (string.IsNullOrEmpty(this.apiKey))
            {
                throw new ArgumentException(SystranMachineTranslationConnector.NoApiKeyExceptionMessage);
            }

            this.apiUrl = config.Get(SystranMachineTranslationConnector.ApiUrl);
            if (string.IsNullOrEmpty(this.apiUrl))
            {
                this.apiUrl = "https://api-translate.systran.net";
            }

            this.httpClient = new HttpClient();
        }
        #endregion

        protected override List<string> Translate(List<string> input, ITranslationOptions translationOptions)
        {
            return TranslateTexts(input, translationOptions.SourceLanguage, translationOptions.TargetLanguage);
        }

        private List<string> TranslateTexts(List<string> texts, string sourceLanguage, string targetLanguage)
        {
            var results = new List<string>(texts.Count);

            foreach (var batch in SplitIntoBatches(texts))
            {
                results.AddRange(TranslateBatch(batch, sourceLanguage, targetLanguage));
            }

            return results;
        }

        private IEnumerable<List<string>> SplitIntoBatches(List<string> texts)
        {
            var batch = new List<string>();
            var batchBytes = 0;

            foreach (var text in texts)
            {
                var textBytes = Encoding.UTF8.GetByteCount(text);

                if (batch.Count > 0 && (batch.Count >= MaxBatchItems || batchBytes + textBytes > MaxBatchBytes))
                {
                    yield return batch;
                    batch = new List<string>();
                    batchBytes = 0;
                }

                batch.Add(text);
                batchBytes += textBytes;
            }

            if (batch.Count > 0)
                yield return batch;
        }

        private List<string> TranslateBatch(List<string> texts, string sourceLanguage, string targetLanguage)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{this.apiUrl}/translation/text/translate");
            request.Headers.Add("Authorization", $"Key {this.apiKey}");

            var requestBody = new JObject
            {
                ["input"] = new JArray(texts),
                ["source"] = GetLanguageCode(sourceLanguage),
                ["target"] = GetLanguageCode(targetLanguage)
            };

            var content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json");
            request.Content = content;

            var response = this.httpClient.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var responseJson = JObject.Parse(responseContent);

            var outputs = responseJson["outputs"] as JArray;
            if (outputs == null || outputs.Count != texts.Count)
                throw new InvalidOperationException($"Unexpected Systran API response format. Response: {responseContent}");

            return outputs.Select(o => o["output"]?.ToString() ?? string.Empty).ToList();
        }

        private string GetLanguageCode(string cultureCode)
        {
            if (string.IsNullOrEmpty(cultureCode))
                return cultureCode;

            if (cultureCode.Length == 2)
                return cultureCode;

            var dashIndex = cultureCode.IndexOf('-');
            if (dashIndex > 0)
                return cultureCode.Substring(0, dashIndex);

            return cultureCode;
       }

        internal const string ConnectorName = "SystranMachineTranslation";
        internal const string ConnectorTitle = "Systran Machine Translation";
        internal const string ApiKey = "apiKey";
        internal const string ApiUrl = "apiUrl";
        internal const string NoApiKeyExceptionMessage = "No API key configured for Systran translations connector.";

        private const int MaxBatchItems = 50000;
        private const int MaxBatchBytes = 40 * 1024 * 1024;

        private HttpClient httpClient;
        private string apiKey;
        private string apiUrl;
    }

    
}
