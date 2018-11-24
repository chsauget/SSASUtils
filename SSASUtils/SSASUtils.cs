using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.DependencyInjection;

namespace SSASUtils
{
    using Infrastructure;
    using Helpers;
    using Infrastructure.Config;
    using Microsoft.Rest;
    using Microsoft.Extensions.Logging;

    public static class SSASUtils
    {
        private static readonly AppSettings Settings =
            ServiceProviderConfiguration.GetServiceProvider().GetService<AppSettings>();

        private static TokenCredentials _tokenCredentials;
        public static TokenCredentials TokenCredentials
        {
            get
            {
                if (_tokenCredentials == null)
                {
                    //Retrieve the access credential
                    TokenCredentials tokenCredentials = ADALHelper.GetSSASToken(
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


            dynamic data = await req.Content.ReadAsAsync<object>();


            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}
