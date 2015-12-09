﻿using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNet.Authentication.JwtBearer;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.OptionsModel;
using Bit.Core.Domains;

namespace Bit.Core.Identity
{
    public class JwtBearerSignInManager
    {
        public JwtBearerSignInManager(
            UserManager<User> userManager,
            IHttpContextAccessor contextAccessor,
            IUserClaimsPrincipalFactory<User> claimsFactory,
            IOptions<IdentityOptions> optionsAccessor,
            IOptions<JwtBearerIdentityOptions> jwtIdentityOptionsAccessor,
            IOptions<JwtBearerOptions> jwtOptionsAccessor,
            ILogger<JwtBearerSignInManager> logger)
        {
            UserManager = userManager;
            Context = contextAccessor.HttpContext;
            ClaimsFactory = claimsFactory;
            IdentityOptions = optionsAccessor?.Value ?? new IdentityOptions();
            JwtIdentityOptions = jwtIdentityOptionsAccessor?.Value ?? new JwtBearerIdentityOptions();
            JwtBearerOptions = jwtOptionsAccessor?.Value ?? new JwtBearerOptions();
        }

        internal UserManager<User> UserManager { get; set; }
        internal HttpContext Context { get; set; }
        internal IUserClaimsPrincipalFactory<User> ClaimsFactory { get; set; }
        internal IdentityOptions IdentityOptions { get; set; }
        internal JwtBearerIdentityOptions JwtIdentityOptions { get; set; }
        internal JwtBearerOptions JwtBearerOptions { get; set; }

        public async Task<ClaimsPrincipal> CreateUserPrincipalAsync(User user) => await ClaimsFactory.CreateAsync(user);

        public Task<bool> ValidateSecurityStampAsync(User user, ClaimsPrincipal principal)
        {
            if(user != null && UserManager.SupportsUserSecurityStamp)
            {
                var securityStamp = principal.FindFirstValue(IdentityOptions.ClaimsIdentity.SecurityStampClaimType);
                if(securityStamp == user.SecurityStamp)
                {
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }

        public async Task<JwtBearerSignInResult> PasswordSignInAsync(User user, string password)
        {
            if(user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            if(await UserManager.CheckPasswordAsync(user, password))
            {
                return await SignInOrTwoFactorAsync(user);
            }

            return JwtBearerSignInResult.Failed;
        }

        public async Task<JwtBearerSignInResult> PasswordSignInAsync(string userName, string password)
        {
            var user = await UserManager.FindByNameAsync(userName);
            if(user == null)
            {
                return JwtBearerSignInResult.Failed;
            }

            return await PasswordSignInAsync(user, password);
        }

        public async Task<JwtBearerSignInResult> TwoFactorSignInAsync(User user, string provider, string code)
        {
            if(user == null)
            {
                return JwtBearerSignInResult.Failed;
            }

            if(await UserManager.VerifyTwoFactorTokenAsync(user, provider, code))
            {
                var token = await SignInAsync(user, false);

                var success = JwtBearerSignInResult.Success;
                success.Token = token;
                success.User = user;

                return success;
            }

            return JwtBearerSignInResult.Failed;
        }

        private async Task<string> SignInAsync(User user, bool twoFactor)
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

            DateTime? tokenExpiration = null;
            var userPrincipal = await CreateUserPrincipalAsync(user);
            if(twoFactor)
            {
                userPrincipal.Identities.First().AddClaim(new Claim(ClaimTypes.AuthenticationMethod, JwtIdentityOptions.TwoFactorAuthenticationMethod));
                if(JwtIdentityOptions.TwoFactorTokenLifetime.HasValue)
                {
                    tokenExpiration = DateTime.UtcNow.Add(JwtIdentityOptions.TwoFactorTokenLifetime.Value);
                }
            }
            else
            {
                userPrincipal.Identities.First().AddClaim(new Claim(ClaimTypes.AuthenticationMethod, JwtIdentityOptions.AuthenticationMethod));
                if(JwtIdentityOptions.TokenLifetime.HasValue)
                {
                    tokenExpiration = DateTime.UtcNow.Add(JwtIdentityOptions.TokenLifetime.Value);
                }
            }

            var securityToken = handler.CreateToken(
                issuer: JwtIdentityOptions.Issuer,
                audience: JwtIdentityOptions.Audience,
                signingCredentials: JwtIdentityOptions.SigningCredentials,
                subject: userPrincipal.Identities.First(),
                expires: tokenExpiration);

            return handler.WriteToken(securityToken);
        }

        private async Task<JwtBearerSignInResult> SignInOrTwoFactorAsync(User user)
        {
            if(UserManager.SupportsUserTwoFactor &&
                await UserManager.GetTwoFactorEnabledAsync(user) &&
                (await UserManager.GetValidTwoFactorProvidersAsync(user)).Count > 0)
            {
                var twoFactorToken = await SignInAsync(user, true);

                var twoFactorResult = JwtBearerSignInResult.TwoFactorRequired;
                twoFactorResult.Token = twoFactorToken;
                twoFactorResult.User = user;

                return twoFactorResult;
            }

            var token = await SignInAsync(user, false);

            var result = JwtBearerSignInResult.Success;
            result.Token = token;
            result.User = user;

            return result;
        }
    }
}
