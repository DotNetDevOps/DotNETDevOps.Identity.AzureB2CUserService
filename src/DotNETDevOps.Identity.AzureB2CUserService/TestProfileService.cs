﻿using IdentityModel;
using IdentityServer4.Extensions;
using IdentityServer4.Models;
using IdentityServer4.Services;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace DotNETDevOps.Identity.AzureB2CUserService
{
    public class TestProfileService : IProfileService
    {
        private readonly TestProfileServiceConfiguration options;
        private readonly AzureB2CUserService azureB2CUserService;
        private readonly IAuthenticationProvider authenticationProvider;
        private readonly IHttpClientFactory httpClientFactory;

        public TestProfileService(IOptions<TestProfileServiceConfiguration> options, AzureB2CUserService azureB2CUserService, IAuthenticationProvider authenticationProvider, IHttpClientFactory httpClientFactory)
        {
            this.options = options.Value ?? throw new ArgumentNullException(nameof(options));
            this.azureB2CUserService = azureB2CUserService ?? throw new ArgumentNullException(nameof(azureB2CUserService));
            this.authenticationProvider = authenticationProvider ?? throw new ArgumentNullException(nameof(authenticationProvider));
            this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }
        public Task IsActiveAsync(IsActiveContext context)
        {
            context.IsActive = true;
            return Task.CompletedTask;
        }
        public static IEnumerable<Claim> GetClaims(AzureB2CUser user)
        {
            yield return new Claim(JwtClaimTypes.Name, user.displayName);

            if (!string.IsNullOrEmpty(user.surname))
                yield return new Claim(JwtClaimTypes.FamilyName, user.surname);

            yield return new Claim(JwtClaimTypes.GivenName, user.givenName);

            if (user.facsimileTelephoneNumber.IsPresent()) 
                yield return new Claim(JwtClaimTypes.PhoneNumber, user.facsimileTelephoneNumber);

           

        }

        private static Claim GetClaim(JObject user, string scope, string prop)
        {
            var value = user.SelectToken(prop)?.ToString();
            if (string.IsNullOrEmpty(value))
                return null;
            return new Claim(scope, value);
        }


        public async Task GetProfileDataAsync(ProfileDataRequestContext context)
        {
            //TODO : No userinfo endpoint, so we have to parse the AAD object to claims. 
            //TODO : Create custome attributes in AAD.
           

            if (context.RequestedClaimTypes.Any())
            {
                if (context.Client.ClientId == "DotNetDevOps.Emails")
                {
                    // context.ValidatedRequest.
                    context.AddRequestedClaims(context.Subject.Claims);

                }

                if (options.Schemes.Contains( context.Subject.GetIdentityProvider()))
                { 
                    var str = await azureB2CUserService.GetUserByObjectIdAsync(context.Subject.GetSubjectId()); 
                    context.AddRequestedClaims(GetClaims(str.Value));

                    if (context.RequestedClaimTypes.Contains("role"))
                    {
                        var roles = await azureB2CUserService.GetUserRolesAsync(context.Subject.GetSubjectId());

                        context.AddRequestedClaims(roles.Value.Select(v => new Claim("role", v)));

                    }
                }

                

                if (context.Subject.GetIdentityProvider() == options.AzureADScheme)
                {

                    string userId = context.Subject.FindFirst("oid")?.Value;
                    if (userId != null)
                    {

                        string requestUrl = $"https://graph.microsoft.com/v1.0/users/{userId}/memberOf?$select=displayName";

                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

                        await authenticationProvider.AuthenticateRequestAsync(request);

                        HttpResponseMessage response = await httpClientFactory.CreateClient().SendAsync(request);


                        var json = JObject.Parse(await response.Content.ReadAsStringAsync());

                        var claims = new List<Claim>() { new Claim("oid", userId) };

                        foreach (var group in json["value"])
                            claims.Add(new Claim("role", group["displayName"].ToString(), System.Security.Claims.ClaimValueTypes.String, "Graph"));

                        context.AddRequestedClaims(claims);


                    }
                }
            }
            //   context.AddRequestedClaims(context.Subject.FindAll(JwtClaimTypes.Role));

        }


    }

}
