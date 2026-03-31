Progress.Sitefinity.Translations.SystranMachineTranslationConnector
===========================================

>**Latest supported version**: Sitefinity CMS 14.1.7800.0

>**Documentation articles**: [Custom translation connector](https://www.progress.com/documentation/sitefinity-cms/custom-translation-connector)

>**IMPORTANT**: This repository may not be compatible with the latest or your current Sitefinity CMS version. If you want to use the repository with a specific Sitefinity CMS version, either upgrade the code from this repository or your Sitefinity CMS project to ensure compatibility.<br/>
The dev team monitors the repository. You can create a GitHub issue to submit feedback or report bugs. Or make a pull request to submit project enhancements or compatibility changes that support new Sitefinity CMS versions.

### Overview

When working with the Sitefinity CMS *Translation* module, you can benefit from a number of translation connectors that you use out-of-the-box with minimum setup. To serve your requirements, you can also implement your own translation connector with custom logic. 

This tutorial provides you with a sample that you use to implement a custom translation connector to work with the *SYSTRAN Pure Neural Server* service. You first create and setup the connector and then use the *Systran Translation .Net Client Library* to implement the overall translation process.   

### Prerequisites
- You must have a Sitefinity CMS license.
- Your Sitefinity CMS website must be in multilingual mode.  
 You have added atleast one additinal language to the website. Otherwise, you will not see the translations screen in the administrations tab of your application.
- You must have obtained API key and API URL from Systran.io https://platform.systran.net/user/admin#/apiKeys.

### Installation

Add the *Systran Machine Translation* sample project to your solution. To do this:

1. Open your Sitefinity CMS solution in Visual Studio.
2. Add `Progress.Sitefinity.Translations.SystranMachineTranslation` project to the same solution.
3. In _SystranMachineTranslation_, add a reference to the `SystranClientTranslationApiLib` assembly.  
 You can download it from Systran Natural Language Processing .NET Client Library from https://github.com/SYSTRAN/translation-api-csharp-client.
4. Ensure `Telerik.Sitefinity.Translations` nuget package is installed in `SystranMachineTranslation`.
5. In _SitefinityWebApp_, add a reference to the *SystranMachineTranslation* project.
6. Build your solution.

### Configure the connector

To configure the *SystranMachineTranslationConnector* connector, perform the following:

1. In Sitefinity CMS, navigate to _Administration >> Settings >> Advanced >> Translations >> Connectors_.
2. Expand the _Parameters_ section of the _SystranMachineTranslation_ connector
3. Enter and save the following _Keys_:   
   - `apiKey`  
    In <i>Value</i>, enter the API key that you obtained from Systran.
   - `apiUrl`  
    This is the Systran API URL. If you do not set it, the system uses the default value *https://api-platform.systran.net*.
3. To enable the connector, under _SystranMachineTranslation_, in input field _Enabled_, enter `true`.
4. Save your changes.

### Limitations

SYSTRAN Pure Neural Server deos not support culture-specific languages. The service supports two-letter ISO language codes, such as _en_, _fr_, _de_, etc. You can use neutral culture, such as _en_, or specify a mapping between the specific culture and neutral culture in the translations advanced settings. To do this, navigate to <i>Administration >> Settings >> Advanced >> Culture mappings</i>.
