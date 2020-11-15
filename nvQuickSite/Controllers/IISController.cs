﻿// Copyright (c) 2016-2020 nvisionative, Inc.
//
// This file is part of nvQuickSite.
//
// nvQuickSite is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// nvQuickSite is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with nvQuickSite.  If not, see <http://www.gnu.org/licenses/>.

namespace nvQuickSite.Controllers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.InteropServices;

    using Microsoft.Web.Administration;
    using nvQuickSite.Controllers.Exceptions;
    using Serilog;

    /// <summary>
    /// Controls IIS operations.
    /// </summary>
    public static class IISController
    {
        /// <summary>
        /// Creates a site in IIS.
        /// </summary>
        /// <param name="siteName">The name of the site.</param>
        /// <param name="installFolder">The path to the hosting folder for the site.</param>
        /// <param name="useSiteSpecificAppPool">A value indicating whether to use a site specific App Pool.</param>
        /// <param name="deleteSiteIfExists">If true will delete and recreate the site.</param>
        internal static void CreateSite(string siteName, string installFolder, bool useSiteSpecificAppPool, bool deleteSiteIfExists)
        {
            Log.Logger.Information("Creating site {siteName} in {installFolder}", siteName, installFolder);
            if (SiteExists(siteName, deleteSiteIfExists))
            {
                Log.Logger.Error("Site {siteName} already exists, aborting operation", siteName);
                throw new SiteExistsException("Site name (" + siteName + ") already exists.") { Source = "Create Site: Site Exists" };
            }

            try
            {
                var bindingInfo = "*:80:" + siteName;

                using (ServerManager iisManager = new ServerManager())
                {
                    Site mySite = iisManager.Sites.Add(siteName, "http", bindingInfo, installFolder + "\\Website");
                    mySite.TraceFailedRequestsLogging.Enabled = true;
                    mySite.TraceFailedRequestsLogging.Directory = installFolder + "\\Logs";
                    mySite.LogFile.Directory = installFolder + "\\Logs" + "\\W3svc" + mySite.Id.ToString(CultureInfo.InvariantCulture);

                    if (useSiteSpecificAppPool)
                    {
                        var appPoolName = siteName + "_nvQuickSite";
                        ApplicationPool newPool = iisManager.ApplicationPools.Add(appPoolName);
                        newPool.ManagedRuntimeVersion = "v4.0";
                        mySite.ApplicationDefaults.ApplicationPoolName = appPoolName;
                    }

                    iisManager.CommitChanges();
                    Log.Logger.Information("Created site {siteName}", siteName);
                }
            }
            catch (Exception ex)
            {
                var message = "Error creating site in IIS";
                Log.Logger.Error(ex, message);
                throw new IISControllerException(message, ex) { Source = "Create Site" };
            }
        }

        /// <summary>
        /// Attempts to delete a site in IIS, optionnally reporting progress.
        /// </summary>
        /// <param name="siteId">The id of the site to delete.</param>
        /// <param name="progress">The progress reporter.</param>
        internal static void DeleteSite(long siteId, IProgress<int> progress = null)
        {
            using (var iisManager = new ServerManager())
            {
                try
                {
                    var site = iisManager.Sites.FirstOrDefault(s => s.Id == siteId);

                    if (site == null)
                    {
                        progress?.Report(100);
                        return;
                    }

                    iisManager.Sites.Remove(site);
                    iisManager.CommitChanges();
                    progress?.Report(100);
                }
                catch (COMException ex)
                {
                    progress?.Report(100);
                    DialogController.ShowMessage(
                        "Site Deletion Error",
                        ex.Message,
                        SystemIcons.Error,
                        DialogController.DialogButtons.OK);
                    return;
                }
            }
        }

        /// <summary>
        /// Attempts to delete an application pool.
        /// </summary>
        /// <param name="appPoolName">The name of the application pool.</param>
        /// <param name="progress">The progress reporter.</param>
        internal static void DeleteAppPool(string appPoolName, IProgress<int> progress)
        {
            try
            {
                using (var iisManager = new ServerManager())
                {
                    var appPool = iisManager.ApplicationPools.FirstOrDefault(a => a.Name == appPoolName);
                    if (appPool == null)
                    {
                        progress?.Report(100);
                        return;
                    }

                    iisManager.ApplicationPools.Remove(appPool);
                    iisManager.CommitChanges();
                    progress?.Report(100);
                }
            }
            catch (COMException ex)
            {
                progress?.Report(100);
                DialogController.ShowMessage(
                    "Delete AppPool Error",
                    ex.Message,
                    SystemIcons.Error,
                    DialogController.DialogButtons.OK);
                return;
            }
        }

        /// <summary>
        /// Gets a list of IIS sites.
        /// </summary>
        /// <param name="createdByThisToolOnly">When true, will filter the results to only show sites created by this tool.</param>
        /// <returns>An enumeration of sites.</returns>
        internal static IEnumerable<Site> GetSites(bool createdByThisToolOnly = false)
        {
            List<Site> sites;
            using (ServerManager iisManager = new ServerManager())
            {
                sites = iisManager.Sites.ToList();
                if (!createdByThisToolOnly)
                {
                    return sites;
                }

                sites = sites
                    .Where(s =>
                        s.ApplicationDefaults.ApplicationPoolName.EndsWith(
                            "_nvQuickSite",
                            StringComparison.Ordinal))
                    .ToList();

                return sites;
            }
        }

        private static bool SiteExists(string siteName, bool deleteSiteIfExists)
        {
            bool exists = false;
            using (ServerManager iisManager = new ServerManager())
            {
                SiteCollection siteCollection = iisManager.Sites;

                foreach (Site site in siteCollection)
                {
                    if (site.Name == siteName.ToString())
                    {
                        exists = true;
                        if (deleteSiteIfExists)
                        {
                            if (site.ApplicationDefaults.ApplicationPoolName == siteName + "_nvQuickSite")
                            {
                                ApplicationPoolCollection appPools = iisManager.ApplicationPools;
                                foreach (ApplicationPool appPool in appPools)
                                {
                                    if (appPool.Name == siteName + "_nvQuickSite")
                                    {
                                        iisManager.ApplicationPools.Remove(appPool);
                                        break;
                                    }
                                }
                            }

                            iisManager.Sites.Remove(site);
                            iisManager.CommitChanges();
                            exists = false;
                            break;
                        }

                        break;
                    }
                    else
                    {
                        exists = false;
                    }
                }
            }

            return exists;
        }
    }
}
