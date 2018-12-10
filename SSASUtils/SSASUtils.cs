using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using Microsoft.Rest;
using Microsoft.Extensions.Logging;

namespace SSASUtils
{
    using Infrastructure;
    using Helpers;
    using Infrastructure.Config;
    using Models;
    using System.Net.Http.Headers;
    using Newtonsoft.Json.Linq;
    using System.Threading;
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using System.Text;

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
        // Request input format exemple :
        //===============================
        //[
        //    {
        //    "serverUrl": "asazure://northeurope.asazure.windows.net/<ServerName>",
        //        "modelName": "<ModelName>",
        //        "resourceGroup" : "<ResourceGroupName>",
        //        "SyncReplicas" : true,
        //        "refreshRequest": {
        //        "type": "full",
        //        "maxParallelism": 10,
        //        "Objects": [
        //            {
        //          "table": "<TableNamme>"

        //          },
        //          {
        //           "table": "<TableNamme>"
        //          }
        //          ]
        //    }
        //},
        //    {
        //    "serverUrl": "asazure://northeurope.asazure.windows.net/<ServerName>",
        //    "modelName": "<ModelName>",
        //    "resourceGroup" : "<ResourceGroupName>",
        //    "SyncReplicas" : false,
        //    "refreshRequest": {
        //    "type": "full",
        //    "maxParallelism": 10
        //    }
        //    }
        //]
        [FunctionName("ProcessModel")]
        public static async Task<HttpResponseMessage> RunProcessModel([HttpTrigger(AuthorizationLevel.Function,"post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");
            bool IsOneFailed = false;
            bool wait = true;
            List<ProcessModel> processQueryList = await req.Content.ReadAsAsync<List<ProcessModel>>();
            List<ProcessState> executionList = new List<ProcessState>();
            List<SyncState> syncList = new List<SyncState>();

            try
            {
                foreach (ProcessModel processQuery in processQueryList)
                {
                    if (processQuery.SyncReplicas)
                    {
                        await configureProcessingServer("ReadOnly", processQuery.resourceGroup, processQuery.serverUrl.Substring(processQuery.serverUrl.LastIndexOf("/")+1), log);
                    }
                    var processUri = await RunProcessModelAsync(processQuery, log);
                    executionList.Add(new ProcessState { wait = true, processUri= processUri, conflic=false, hasError=false, serverUrl= processQuery.serverUrl, modelName= processQuery.modelName, syncReplicas = processQuery.SyncReplicas, resourceGroup = processQuery.resourceGroup });
                }
                
                foreach(ProcessState process in executionList)
                {
                    Uri processUri = process.processUri;

                    if (processUri.OriginalString == "https://InProgress")
                    {
                        // if status is in progress ==> Do nothing
                        process.wait = false;
                        process.conflic = true;
                    }
                    else if (processUri.OriginalString == "https://Error")
                    {
                        process.hasError = true;
                        process.errorMessage = "Process Exception - Error with Azure Analysis Service Server";
                    }
                }

                // Wait for the process

                wait = executionList.Where(p => p.wait == true).Count() > 0;
                while (wait)
                {
                    Thread.Sleep(5000);

                    // For each process 

                    foreach (var process in executionList.Where(p => p.wait == true))
                    {
                        var output = await CheckProcessStatusRestAPI(process.processUri, log);
                        if (output.Key == "succeeded")
                        {
                            process.wait = false;
                            log.Info(string.Format("Cube {0}/{1} Processing End", process.serverUrl, process.modelName));
                            //UpdateMessage(logIdFinance, 20, string.Empty, 0);

                            // IF is this server is replicated ==> Sync other DB
                            if (process.syncReplicas)
                            {
                                await configureProcessingServer("All", process.resourceGroup, process.serverUrl.Substring(process.serverUrl.LastIndexOf("/") + 1), log);
                                //logIdsyncFinance = LogMessage(ExecutionNumber, 10, 202);

                                SyncState state = new SyncState();
                                state.syncUri = await SyncRestAPI(process.serverUrl, process.modelName, log);
                                state.syncWait = true;
                                syncList.Add(state);
                            }
                        }
                        else if (output.Key == "failed" || output.Key == "cancelled")
                        {
                            process.wait = false;
                            process.hasError = true;
                            process.errorMessage = string.Format("{0} - {1} Cube Processing Failed", process.serverUrl, output.Value);
                            log.Error(process.errorMessage);
                            //UpdateMessage(logIdFinance, 30, "Process Failed", 0);

                            IsOneFailed = true;

                            if (process.syncReplicas)
                            {
                                await configureProcessingServer("All", process.resourceGroup, process.serverUrl.Substring(process.serverUrl.LastIndexOf("/") + 1), log);
                            }
                        }
                    }
                    wait = executionList.Where(p => p.wait == true).Count() > 0;
                }

                // wait for synchronisation

                bool waitsync = syncList.Where(s => s.syncWait == true).Count() > 0;
                while (waitsync)
                {
                    Thread.Sleep(5000);
                    foreach (SyncState syncState in syncList)
                    {
                        var output = await CheckSyncStatusRestAPI(syncState.syncUri, log);
                        if (output == "succeeded")
                        {
                            syncState.syncWait = false;
                            log.Info(String.Format("{0} Cube synchronization End", syncState.modelName));
                        }
                        else if (output == "failed")
                        {
                            syncState.syncWait = false;
                            log.Error(String.Format("{0} Cube synchronization Failed", syncState.modelName));
                            syncState.hasError = true;
                        }
                    }
                    waitsync = syncList.Where(s => s.syncWait == true).Count() > 0;
                }
            }
            catch (Exception e)
            {
                string message = e.Message.Substring(1, e.Message.Length <= 500 ? e.Message.Length - 1 : 500);
                log.Error($"Exception: {message} - {e.Source} - {e.InnerException}");
                IsOneFailed = true;
            }

            List<ProcessResult> result = new List<ProcessResult>();
            foreach (var p in executionList)
            {
                if (p.hasError)
                    result.Add(new ProcessResult { serverUrl = p.serverUrl, modelName = p.modelName, State = "Error", errorMessage = p.errorMessage });
                else if (p.conflic)
                    result.Add(new ProcessResult { serverUrl = p.serverUrl, modelName = p.modelName, State = "Already processing => Conflict" });
                else 
                    result.Add(new ProcessResult { serverUrl = p.serverUrl, modelName = p.modelName, State = "Succeded" });
            }
            string messageResult = JsonConvert.SerializeObject(result);
            if (IsOneFailed == false)
                return req.CreateResponse(HttpStatusCode.OK, messageResult);             
            else
                return req.CreateResponse(HttpStatusCode.InternalServerError, messageResult);
        }

        private static async Task<Uri> RunProcessModelAsync(ProcessModel processQuery, TraceWriter log)
        {
            try
            {
                HttpClient client = new HttpClient();
                client.BaseAddress = UtilityHelper.ServerNameToUri(processQuery.serverUrl, processQuery.modelName);

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenCredentials);

                HttpResponseMessage response = await client.PostAsJsonAsync("refreshes", processQuery.refreshRequest);

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    log.Warning("Process already in progress !!!");
                    Uri retWarn = new Uri("https://InProgress");
                    return retWarn;
                }
                else if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Accepted)
                {
                    Uri location = response.Headers.Location;
                    log.Info(location.OriginalString);
                    return location;
                }
                else
                {
                    log.Error("Request not completed : StatusCode " + response.StatusCode + ", Reason " + response.ReasonPhrase + " !!!");
                    Uri retWarn = new Uri("https://Error");
                    return retWarn;
                }
            }
            catch (Exception e){
                log.Error(e.Message);
                return null;
            }
        }

        public static async Task<Uri> SyncRestAPI(string serverName, string modelName, TraceWriter log)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = UtilityHelper.ServerNameToUri(serverName, modelName);

            // Send refresh request
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenCredentials);

            try
            {
                log.Info(string.Format("Start Process for {0} on {1}", modelName, serverName));
                HttpResponseMessage response = await client.PostAsync("sync", null);

                if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Accepted)
                {
                    Uri location = response.Headers.Location;
                    log.Info(location.OriginalString);
                    return location;
                }
                else
                {
                    log.Error("Request not completed : StatusCode " + response.StatusCode + ", Reason " + response.ReasonPhrase + " !!!");
                    Uri retWarn = new Uri("https://Error");
                    return retWarn;
                }
            }
            catch (Exception e)
            {
                log.Error(e.Message);
                return null;
            }
        }

        private static async Task<KeyValuePair<string,string>> CheckProcessStatusRestAPI(Uri location, TraceWriter log)
        {
            string output = "";
            HttpClient client = new HttpClient();
            try
            {
                // Refresh token if required
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenCredentials);

                HttpResponseMessage response = await client.GetAsync(location);
                if (response.IsSuccessStatusCode)
                {
                    output = await response.Content.ReadAsStringAsync();
                }
                else
                {
                    log.Warning("response.IsSuccessStatusCode is false for " + location.AbsoluteUri.ToString());
                    return new KeyValuePair<string, string>(null, null);
                }

                JObject obj = JObject.Parse(output);
                string returnStatus = obj.GetValue("status").ToString();
                string error = "";

                if (returnStatus == "failed" || returnStatus == "cancelled")
                {
                    error = obj.GetValue("messages").ToString();
                    log.Error("Process Error Messages: " + obj.GetValue("messages").ToString());
                }
                return new KeyValuePair<string, string>(returnStatus, error);
                //succeeded
                //inProgress
            }
            catch (TaskCanceledException ex)
            {
                log.Warning("TaskCanceledException occured for " + location.AbsoluteUri.ToString());
                // Check ex.CancellationToken.IsCancellationRequested here.
                // If false, it's pretty safe to assume it was a timeout.
                if (ex.CancellationToken.IsCancellationRequested)
                    log.Warning("CancellationToken.IsCancellationRequested is true for " + location.AbsoluteUri.ToString());

                return new KeyValuePair<string, string>("RequestCancelled",ex.Message);
            }
        }

        /// <summary>
        /// Configure the querypoolConnectionMode of the SSAS Instance in a Scale-out mode
        /// </summary>
        /// <param name="queryPoolMode">All or ReadOnly</param>
        /// <param name="resourceGroup">Name of the resourceGroup</param>
        /// <param name="servername">Name of the AAS instance</param>
        /// <param name="log"></param>
        /// <returns></returns>
        private static async Task configureProcessingServer(string queryPoolMode, string resourceGroup, string servername, TraceWriter log)
        {
            log.Info("Start to configure querypoolConnectionMode to " + queryPoolMode + " for " + servername);
            string output = "";
            HttpClient client = new HttpClient();
            try
            {
                // Refresh token if required
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenCredentials);

                string urlrest = "https://management.azure.com/subscriptions/"+ Settings.SubscriptionId + "/resourceGroups/" + resourceGroup + "/providers/Microsoft.AnalysisServices/servers/" + servername + "?api-version=2017-08-01";

                client.BaseAddress = new Uri(urlrest);


                UpdateRequest updateRequest = new UpdateRequest()
                {
                    properties = new UpdateProperties()
                    {
                        querypoolConnectionMode = queryPoolMode
                    }
                };
                var udpj = JsonConvert.SerializeObject(updateRequest);

                HttpRequestMessage request = new HttpRequestMessage
                {
                    Method = new HttpMethod("PATCH"),
                    RequestUri = client.BaseAddress,
                    Content = new StringContent(udpj, Encoding.UTF8, "application/json")
                };

                HttpResponseMessage response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    output = await response.Content.ReadAsStringAsync();
                    //  log.Info(output);
                }
                else
                {
                    log.Warning("response.IsSuccessStatusCode is false for " + servername);
                }

                Thread.Sleep(5000);

            }
            catch (TaskCanceledException ex)
            {
                log.Warning("TaskCanceledException occured for " + servername);
                // Check ex.CancellationToken.IsCancellationRequested here.
                // If false, it's pretty safe to assume it was a timeout.
                if (ex.CancellationToken.IsCancellationRequested)
                    log.Warning("CancellationToken.IsCancellationRequested is true for " + servername);
            }
            catch (Exception e)
            {
                log.Error("QueryPool Error" + e.Message);
            }
        }

        private static async Task<string> CheckSyncStatusRestAPI(Uri location, TraceWriter log, bool retry = false)
        {
            string output = "";
            HttpClient client = new HttpClient();
            try
            {
                // Refresh token if required
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenCredentials);

                HttpResponseMessage response = await client.GetAsync(location);
                if (response.IsSuccessStatusCode)
                {
                    output = await response.Content.ReadAsStringAsync();
                }
                else
                {
                    log.Warning("response.IsSuccessStatusCode is false for " + location.AbsoluteUri.ToString());
                    return null;
                }

                JObject obj = JObject.Parse(output);
                int returnStatus;
                int.TryParse(obj.GetValue("syncstate").ToString(), out returnStatus);

                // 0: Replicating. Database files are being replicated to a target folder.
                // 1: Rehydrating. The database is being rehydrated on read-only server instance(s).
                // 2: Completed. The sync operation completed successfully.
                // 3: Failed. The sync operation failed.
                // 4: Finalizing. The sync operation has completed but is performing clean up steps.

                if (returnStatus == 3)
                {
                    log.Error("Process Error  Messages: " + obj.GetValue("details").ToString());
                    if (retry)
                        return "failed";
                    else
                    {
                        // i sync status = false => wait 1 minute and check with url without operationId
                        Uri newURI = new Uri(location.AbsoluteUri.Substring(0, location.AbsoluteUri.LastIndexOf("?")));
                        System.Threading.Thread.Sleep(60000);
                        return await CheckSyncStatusRestAPI(newURI, log, true);
                    }             
                }
                else if (returnStatus == 2)
                    return "succeeded";
                else return "inProgress";
                //succeeded
                //inProgress
            }
            catch (TaskCanceledException ex)
            {
                log.Warning("TaskCanceledException occured for " + location.AbsoluteUri.ToString());
                // Check ex.CancellationToken.IsCancellationRequested here.
                // If false, it's pretty safe to assume it was a timeout.
                if (ex.CancellationToken.IsCancellationRequested)
                {
                    log.Warning("CancellationToken.IsCancellationRequested is true for " + location.AbsoluteUri.ToString());
                }
                return "RequestCancelled";
            }
        }


    }

    class ProcessState
    {
        public string serverUrl { get; set; }
        public string modelName { get; set; }
        public string resourceGroup { get; set; }
        public bool syncReplicas { get; set; }
        public bool wait { get; set; }
        public Uri processUri { get; set; }
        public bool conflic { get; set; }
        public bool hasError { get; set; }
        public string errorMessage { get; set; }
    }

    class SyncState {
        public string serverUrl { get; set; }
        public string modelName { get; set; }
        public bool syncWait { get; set; }
        public Uri syncUri { get; set; }
        public bool hasError { get; set; }
        public string errorMessage { get; set; }
    }


    class ProcessResult {
        public string serverUrl { get; set; }
        public string modelName { get; set; }
        public string State { get; set; }
        public string errorMessage { get; set; }
    }

    //Class for configure the process server in the query pool
    class UpdateRequest
    {
        public UpdateProperties properties { get; set; }
    }

    class UpdateProperties
    {
        public string querypoolConnectionMode { get; set; }
    }
}
