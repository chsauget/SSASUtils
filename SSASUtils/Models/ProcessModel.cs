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
        public string serverName { get; set; }
        public string modelName { get; set; }

        public class RefreshRequest
        {
            public string type { get; set; }
            public int maxParallelism { get; set; }
        }
    }
}
