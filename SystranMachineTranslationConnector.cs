using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Progress.Sitefinity.Translations;
using Systran;
using Systran.TranslationClientLib.Api;
using Systran.TranslationClientLib.Client;
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
            var key = config.Get(SystranMachineTranslationConnector.ApiKey);
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException(SystranMachineTranslationConnector.NoApiKeyExceptionMessage);
            }

            var apiUrl = "https://api-platform.systran.net";
            if (!string.IsNullOrEmpty(config.Get(SystranMachineTranslationConnector.ApiUrl)))
            {
                apiUrl = config.Get(SystranMachineTranslationConnector.ApiUrl);
            }

            this.client = new ApiClient(apiUrl);
            Configuration.apiClient = client;
            Dictionary<String, String> keys = new Dictionary<String, String>();
           
            keys.Add("key", key);
            Configuration.apiKey = keys;
            if (keys.Count == 0)
                throw new Exception("No api key found, please check your apiKey.txt file");

            this.translationApi = new TranslationApi(Configuration.apiClient);

        }
        #endregion

        protected override List<string> Translate(List<string> input, ITranslationOptions translationOptions)
        {
            var output = new List<string>();
            foreach (var item in input)
            {
                var response = this.translationApi.TranslationTranslateGet(new List<string>() { item }, translationOptions.SourceLanguage, translationOptions.TargetLanguage, null, null, true, false, null, null, false, null, null, false, null, null);
                output.Add(response.Outputs.First().Output);
            }

            return output;
        }

        internal const string ConnectorName = "SystranMachineTranslation";
        internal const string ConnectorTitle = "Systran Machine Translation";
        internal const string ApiKey = "apiKey";
        internal const string ApiUrl = "apiUrl";
        internal const string NoApiKeyExceptionMessage = "No API key configured for azure translations connector.";

        private ApiClient client;
        private TranslationApi translationApi;
    }
}
