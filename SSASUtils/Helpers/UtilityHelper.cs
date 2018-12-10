using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SSASUtils.Helpers
{
    class UtilityHelper
    {
        public static Uri ServerNameToUri(string serverName, string model)
        {
            var u = new Uri(serverName);
            string uri = "https://<url>/servers/<serverName>/models/<resource>/".Replace("<url>", u.Host).Replace("<serverName>", u.AbsolutePath.Substring(1)).Replace("<resource>",model);
            return new Uri(uri);
        }        
    }


}
