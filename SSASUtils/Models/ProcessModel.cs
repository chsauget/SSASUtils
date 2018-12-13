using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SSASUtils.Models
{
    class ProcessModel
    {
        public RefreshRequest refreshRequest { get; set; }
        public string serverUrl { get; set; }
        public string modelName { get; set; }
        public string resourceGroup { get; set; }
        public bool SyncReplicas { get; set; }

        public class RefreshRequest
        {
            public string Type { get; set; }
            public int MaxParallelism { get; set; }
            public ObjectToProcess[] Objects { get; set; }           
        }

        public class ObjectToProcess {
            public string table { get; set; }
            public string partition { get; set; }
        }
    }


    class ProcessState
    {
        public ProcessModel process { get; set; }
        public bool wait { get; set; }
        public Uri processUri { get; set; }
        public bool conflic { get; set; }
        public bool hasError { get; set; }
        public string errorMessage { get; set; }
    }

    class SyncState
    {
        public ProcessModel process { get; set; }
        public bool syncWait { get; set; }
        public Uri syncUri { get; set; }
        public bool hasError { get; set; }
        public string errorMessage { get; set; }
    }


    class ProcessResult
    {
        public string serverUrl { get; set; }
        public string syncState { get; set; }
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
