using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using System;
using System.Threading.Tasks;

namespace SSASUtils.Helpers
{
    public static class ADALHelper
    {
        //Retrieve the ADAL Token 
        public static string GetSSASToken(string resourceURI, string authority, string clientId,  string AppSecret)
        {
            ClientCredential credential = new ClientCredential(clientId, AppSecret);
            // Authenticate using created credentials
            AuthenticationContext authenticationContext = new AuthenticationContext(authority);

            Task<AuthenticationResult> authenticationResultTask = authenticationContext.AcquireTokenAsync(resourceURI, credential);

            AuthenticationResult authenticationResult = authenticationResultTask.Result;
            if (authenticationResult == null)
            {
                throw new Exception("Authentication Failed.");
            }
            else
            {
                return authenticationResult.AccessToken;
            }
        }

    }
}
