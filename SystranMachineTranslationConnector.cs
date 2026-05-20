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
            var output = new List<string>();
            foreach (var item in input)
            {
                var translatedText = TranslateText(item, translationOptions.SourceLanguage, translationOptions.TargetLanguage);
                output.Add(translatedText);
            }

            return output;
        }

        private string TranslateText(string text, string sourceLanguage, string targetLanguage)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{this.apiUrl}/translation/text/translate");
            request.Headers.Add("Authorization", $"Key {this.apiKey}");

            var requestBody = new JObject
            {
                ["input"] = text,
                ["source"] = sourceLanguage,
                ["target"] = targetLanguage
            };

            var content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json");
            request.Content = content;

            var response = this.httpClient.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var responseJson = JObject.Parse(responseContent);

            return responseJson["output"].ToString();
        }

        internal const string ConnectorName = "SystranMachineTranslation";
        internal const string ConnectorTitle = "Systran Machine Translation";
        internal const string ApiKey = "apiKey";
        internal const string ApiUrl = "apiUrl";
        internal const string NoApiKeyExceptionMessage = "No API key configured for Systran translations connector.";

        private HttpClient httpClient;
        private string apiKey;
        private string apiUrl;
    }
}
