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
            public string type { get; set; }
            public int maxParallelism { get; set; }
            public ObjectToProcess[] Objects { get; set; }           
        }

        public class ObjectToProcess {
            public string table { get; set; }
            public string partition { get; set; }
        }
    }
}
