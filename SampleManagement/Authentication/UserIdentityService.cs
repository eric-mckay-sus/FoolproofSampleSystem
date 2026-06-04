// <copyright file="UserIdentityService.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement.Authentication;

using System.Runtime.InteropServices;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ENV = Environment;

/// <summary>
/// Provides a cached identity for the current application user.
/// </summary>
public interface IUserIdentityService
{
    /// <summary>
    /// Gets the current user's claims principal from the environment and database.
    /// </summary>
    /// <returns>The current user's claims principal.</returns>
    Task<ClaimsPrincipal> GetUserPrincipalAsync();
}

/// <summary>
/// Resolves the current associate's identity from Windows login and the sample database.
/// </summary>
/// <param name="dbFactory">Database context factory for reading associate data.</param>
/// <param name="cache">Cache used to store the resolved ClaimsPrincipal.</param>
public class UserIdentityService(IDbContextFactory<FPSampleDbContext> dbFactory, IMemoryCache cache) : IUserIdentityService
{
    private readonly IDbContextFactory<FPSampleDbContext> dbFactory = dbFactory; // shadowing the parameter name with the field name is okay because we always qualify with 'this'
    private readonly IMemoryCache cache = cache;

    /// <summary>
    /// Gets the authentication state, which can be one of three things:
    /// Unauthenticated (associate number not in DB, HTTP 401 on attempted approver page access).
    /// Unauthorized (associate number in DB without approver privileges, HTTP 403 on attempted approver page access).
    /// Authorized (associate number in DB with approver privileges, successful navigation on approver page access).
    /// </summary>
    /// <returns>The authentication state of the current user.</returns>
    public async Task<ClaimsPrincipal> GetUserPrincipalAsync()
    {
        // Get the username from the system (e.g. SUSU1057, SUSD5938)
        string? associateString = ENV.UserName;
        string cacheKey = $"UserPrincipal_{associateString}";

        // If there's an identity stored in the cache, use that
        if (this.cache.TryGetValue(cacheKey, out ClaimsPrincipal? cachedPrincipal) && cachedPrincipal != null)
        {
            return cachedPrincipal;
        }

        associateString = ReadUsername(associateString);

        if (associateString == null)
        {
            return new (new ClaimsIdentity());
        }

        // Trim the first four characters (SUSU, but also works for SUSD if an IT person wanted to peek).
        associateString = associateString[4..];

        ClaimsPrincipal principal;

        // Extract associate number from remaining (hopefully all numeric) characters. Note this works for any length of associate number that fits in an int (guaranteed up to 9 digits)
        if (int.TryParse(associateString, out int associateNum))
        {
            // Lookup associate from DB
            using FPSampleDbContext context = await this.dbFactory.CreateDbContextAsync();
            Associate? associate = await context.Set<Associate>()
                .FirstOrDefaultAsync(a => a.AssociateNum == associateNum);

            // The associate was found in the DB
            if (associate != null)
            {
                // Create identifier, adding approver role if applicable
                List<Claim> claims =
                [
                    new (ClaimTypes.Name, associate.Name ?? string.Empty),
                    new ("AssociateNum", associateNum.ToString()),
                ];

                if (associate.IsApprover)
                {
                    claims.Add(new Claim(ClaimTypes.Role, "Approver"));
                }

                if (associate.IsAdmin)
                {
                    claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                }

                ClaimsIdentity identity = new (claims, "AutoAuth");
                principal = new (identity);
            }

            // If the associate wasn't found in the DB, return anonymous
            else
            {
                principal = new (new ClaimsIdentity());
            }
        }

        // If int.TryParse failed (prefix strip didn't get something that looked like associate number), return anonymous.
        else
        {
            principal = new (new ClaimsIdentity());
        }

        this.cache.Set(cacheKey, principal, TimeSpan.FromMinutes(10));
        return principal;
    }

    /// <summary>
    /// Validates a user based on the domain and username. If it's not a match for Stanley, return null.
    /// </summary>
    /// <param name="associateString">The username to validate.</param>
    /// <returns>A string with the username (adapted to Windows, if applicable) if it matches Stanley credentials. Otherwise, null.</returns>
    private static string? ReadUsername(string associateString)
    {
        // Check domain and name, verify that they match for SUS (ignore environment variable)
        // This is not trivial to trick using the environment variables
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] domainAndUser = System.Security.Principal.WindowsIdentity.GetCurrent().Name.Split('\\');
            string domain = domainAndUser[0];
            associateString = domainAndUser[1];
            if (!(domain.Equals("STANLEYUS") && associateString.StartsWith("SUS")))
            {
                return null;
            }
        }

        // Fall back to environment variables if not on Windows
        else
        {
            if (!(ENV.UserDomainName.Equals("STANLEYUS") && associateString.StartsWith("SUS")))
            {
                return null;
            }
        }

        return associateString;
    }
}
