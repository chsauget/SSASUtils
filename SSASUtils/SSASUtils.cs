using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.DependencyInjection;
using System;
using Microsoft.Rest;
using Microsoft.Extensions.Logging;

namespace SSASUtils
{
    using Infrastructure;
    using Helpers;
    using Infrastructure.Config;
    using Models;
    using System.Net.Http.Headers;

    public static class SSASUtils
    {
        private static readonly AppSettings Settings =
            ServiceProviderConfiguration.GetServiceProvider().GetService<AppSettings>();

        private static string _tokenCredentials;
        public static string TokenCredentials
        {
            get
            {
                if (_tokenCredentials == null)
                {
                    //Retrieve the access credential
                    string tokenCredentials = ADALHelper.GetSSASToken(
                        Settings.AzureAd.ResourceURI,
                        Settings.AzureAd.Authority,
                        Settings.AzureAd.ClientId,
                        Settings.AzureAd.AppSecret);

                    _tokenCredentials = tokenCredentials;
                }

                return _tokenCredentials;
            }
        }

        [FunctionName("ProcessModel")]
        public static async Task<HttpResponseMessage> RunProcessModel([HttpTrigger(AuthorizationLevel.Function,"post", Route = null)]HttpRequestMessage req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            HttpClient client = new HttpClient();
            
            ProcessModel processQuery = await req.Content.ReadAsAsync<ProcessModel>();

            client.BaseAddress = UtilityHelper.ServerNameToUri(processQuery.serverName, processQuery.modelName);

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenCredentials);

            HttpResponseMessage response = await client.PostAsJsonAsync("refreshes", processQuery.refreshRequest);


            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}
