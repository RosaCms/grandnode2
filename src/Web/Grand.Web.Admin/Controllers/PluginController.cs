﻿using Grand.Business.Core.Interfaces.Common.Localization;
using Grand.Domain.Permissions;
using Grand.Infrastructure;
using Grand.Infrastructure.Configuration;
using Grand.Infrastructure.Plugins;
using Grand.SharedKernel.Extensions;
using Grand.Web.Admin.Extensions;
using Grand.Web.Admin.Extensions.Mapping;
using Grand.Web.Admin.Models.Plugins;
using Grand.Web.Common.DataSource;
using Grand.Web.Common.Localization;
using Grand.Web.Common.Security.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Reflection;

namespace Grand.Web.Admin.Controllers;

[PermissionAuthorize(PermissionSystemName.Plugins)]
public class PluginController(
    ITranslationService translationService,
    ILogger<PluginController> logger,
    IHostApplicationLifetime applicationLifetime,
    IContextAccessor contextAccessor,
    IServiceProvider serviceProvider,
    IEnumTranslationService enumTranslationService,
    IWebHostEnvironment webHostEnvironment,
    ExtensionsConfig extConfig)
    : BaseAdminController
{
    #region Utilities

    private readonly string PluginsPath = Path.Combine(webHostEnvironment.ContentRootPath, CommonPath.Plugins);

    [NonAction]
    protected virtual PluginModel PreparePluginModel(PluginInfo PluginInfo)
    {
        var pluginModel = PluginInfo.ToModel();
        //logo
        pluginModel.LogoUrl = PluginInfo.GetLogoUrl(contextAccessor.StoreContext.CurrentHost.Url);

        //configuration URLs
        if (PluginInfo.Installed)
        {
            var pluginInstance = PluginInfo.Instance(serviceProvider);
            pluginModel.ConfigurationUrl = pluginInstance.ConfigurationUrl();
        }

        return pluginModel;
    }

    /// <summary>
    ///     Depth-first recursive delete, with handling for descendant directories open in Windows Explorer.
    /// </summary>
    /// <param name="path">Directory path</param>
    protected void DeleteDirectory(string path)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(path);

        // Ensure the path is within the PluginsPath
        if (!path.StartsWith(PluginsPath, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Attempt to delete a directory outside of the plugins path.");

        foreach (var directory in Directory.GetDirectories(path))
            DeleteDirectory(directory);

        if (Directory.Exists(path))
            Directory.Delete(path, true);

    }

    protected static byte[] ToByteArray(Stream stream)
    {
        using (stream)
        {
            using (var memStream = new MemoryStream())
            {
                stream.CopyTo(memStream);
                return memStream.ToArray();
            }
        }
    }

    #endregion

    #region Methods

    public IActionResult Index()
    {
        return RedirectToAction("List");
    }

    public IActionResult List()
    {
        var model = new PluginListModel {
            //load modes
            AvailableLoadModes = enumTranslationService.ToSelectList(LoadPluginsStatus.All, false).ToList()
        };

        return View(model);
    }

    [HttpPost]
    public IActionResult ListSelect(DataSourceRequest command, PluginListModel model)
    {
        var pluginInfos = PluginManager.ReferencedPlugins.ToList();
        var loadMode = (LoadPluginsStatus)model.SearchLoadModeId;
        switch (loadMode)
        {
            case LoadPluginsStatus.InstalledOnly:
                pluginInfos = pluginInfos.Where(x => x.Installed).ToList();
                break;
            case LoadPluginsStatus.NotInstalledOnly:
                pluginInfos = pluginInfos.Where(x => !x.Installed).ToList();
                break;
        }

        var items = new List<PluginModel>();
        foreach (var item in pluginInfos.OrderBy(x => x.Group)) items.Add(PreparePluginModel(item));

        var gridModel = new DataSourceResult {
            Data = items,
            Total = pluginInfos.Count
        };
        return Json(gridModel);
    }

    [HttpPost]
    public async Task<IActionResult> Install(string systemName)
    {
        try
        {
            var pluginInfo = PluginManager.ReferencedPlugins.FirstOrDefault(x => x.SystemName == systemName);
            if (pluginInfo == null)
                //No plugin found with the specified id
                return RedirectToAction("List");

            if (pluginInfo.SupportedVersion != GrandVersion.SupportedPluginVersion)
            {
                Error("You can't install unsupported version of plugin");
                return RedirectToAction("List");
            }

            //check whether plugin is not installed
            if (pluginInfo.Installed)
                return RedirectToAction("List");

            //install plugin
            var plugin = pluginInfo.Instance<IPlugin>(serviceProvider);
            await plugin.Install();

            Success(translationService.GetResource("Admin.Plugins.Installed"));

            logger.LogInformation("The plugin has been installed by the user {CurrentCustomerEmail}",
                contextAccessor.WorkContext.CurrentCustomer.Email);

            //stop application
            applicationLifetime.StopApplication();
        }
        catch (Exception exc)
        {
            Error(exc);
        }

        return RedirectToAction("List");
    }

    [HttpPost]
    public async Task<IActionResult> Uninstall(string systemName)
    {
        try
        {
            var pluginInfo = PluginManager.ReferencedPlugins.FirstOrDefault(x => x.SystemName == systemName);
            if (pluginInfo == null)
                //No plugin found with the specified id
                return RedirectToAction("List");

            //check whether plugin is installed
            if (!pluginInfo.Installed)
                return RedirectToAction("List");

            //uninstall plugin
            var plugin = pluginInfo.Instance<IPlugin>(serviceProvider);
            await plugin.Uninstall();

            Success(translationService.GetResource("Admin.Plugins.Uninstalled"));

            logger.LogInformation("The plugin has been uninstalled by the user {CurrentCustomerEmail}",
                contextAccessor.WorkContext.CurrentCustomer.Email);

            //stop application
            applicationLifetime.StopApplication();
        }
        catch (Exception exc)
        {
            Error(exc);
        }

        return RedirectToAction("List");
    }

    [HttpPost]
    public IActionResult Remove(string systemName)
    {
        if (extConfig.DisableUploadExtensions)
        {
            Error("Upload plugins is disable");
            return RedirectToAction("List");
        }

        try
        {
            var pluginInfo = PluginManager.ReferencedPlugins.FirstOrDefault(x => x.SystemName == systemName);
            if (pluginInfo == null)
                //No plugin found with the specified id
                return RedirectToAction("List");

            DeleteDirectory(pluginInfo.OriginalAssemblyFile.DirectoryName);

            //uninstall plugin
            Success(translationService.GetResource("Admin.Plugins.Removed"));

            logger.LogInformation("The plugin has been removed by the user {CurrentCustomerEmail}",
                contextAccessor.WorkContext.CurrentCustomer.Email);

            //stop application
            applicationLifetime.StopApplication();
        }
        catch (Exception exc)
        {
            Error(exc);
        }

        return RedirectToAction("List");
    }

    public IActionResult ReloadList()
    {
        logger.LogInformation("Reload list of plugins by the user {CurrentCustomerEmail}",
            contextAccessor.WorkContext.CurrentCustomer.Email);

        //stop application
        applicationLifetime.StopApplication();
        return RedirectToAction("List");
    }


    [HttpPost]
    public IActionResult UploadPlugin(IFormFile zippedFile)
    {
        if (extConfig.DisableUploadExtensions)
        {
            Error("Upload plugins is disable");
            return RedirectToAction("List");
        }

        if (zippedFile == null || zippedFile.Length == 0)
        {
            Error(translationService.GetResource("Admin.Common.UploadFile"));
            return RedirectToAction("List");
        }
        var tempDirectory = Path.Combine(webHostEnvironment.ContentRootPath, CommonPath.Plugins, CommonPath.TmpUploadPath);
        var zipFilePath = "";
        try
        {
            if (!Path.GetExtension(zippedFile.FileName).Equals(".zip", StringComparison.InvariantCultureIgnoreCase))
                throw new Exception("Only zip archives are supported");

            // Ensure that temp directory is created
            Directory.CreateDirectory(new DirectoryInfo(tempDirectory).FullName);

            // Generate a unique file name for the uploaded file
            var uniqueFileName = Guid.NewGuid().ToString() + ".zip";
            zipFilePath = Path.Combine(tempDirectory, uniqueFileName);

            // Copy original archive to the temp directory
            using (var fileStream = new FileStream(zipFilePath, FileMode.Create))
            {
                zippedFile.CopyTo(fileStream);
            }

            Upload(zipFilePath);

            var message = translationService.GetResource("Admin.Plugins.Uploaded");
            Success(message);
        }
        finally
        {
            // Delete temporary file
            DeleteDirectory(tempDirectory);
        }

        logger.LogInformation("The plugin has been uploaded by the user {CurrentCustomerEmail}",
            contextAccessor.WorkContext.CurrentCustomer.Email);

        //stop application
        applicationLifetime.StopApplication();

        return RedirectToAction("List");
    }

    private void Upload(string archivePath)
    {
        var pluginsPath = Path.Combine(webHostEnvironment.ContentRootPath, CommonPath.Plugins);
        var uploadedItemDirectoryName = "";
        PluginInfo _pluginInfo = null;
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
        {
            var rootDirectories = archive.Entries.Where(entry =>
                entry.FullName.Count(ch => ch == '/') == 1 && entry.FullName.EndsWith("/")).ToList();
            if (rootDirectories.Count != 1)
                throw new Exception(
                    "The archive should contain only one root plugin. For example, Payments.StripeCheckout.");

            //get directory name (remove the ending /)
            uploadedItemDirectoryName = rootDirectories.First().FullName.TrimEnd('/');

            var supportedVersion = false;
            var _fpath = "";
            foreach (var entry in archive.Entries.Where(x => x.FullName.Contains(".dll")))
            {
                using var unzippedEntryStream = entry.Open();
                try
                {
                    var assembly = Assembly.Load(ToByteArray(unzippedEntryStream));
                    var pluginInfo = assembly.GetCustomAttribute<PluginInfoAttribute>();
                    if (pluginInfo != null && pluginInfo.SupportedVersion == GrandVersion.SupportedPluginVersion)
                    {
                        supportedVersion = true;
                        _fpath = entry.FullName[..entry.FullName.LastIndexOf("/", StringComparison.Ordinal)];
                        archive.Entries.Where(x => !x.FullName.Contains(_fpath)).ToList()
                            .ForEach(y => { archive.GetEntry(y.FullName)!.Delete(); });

                        _pluginInfo = new PluginInfo();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, ex.Message);
                }
            }

            if (!supportedVersion)
                throw new Exception(
                    $"This plugin doesn't support the current version - {GrandVersion.SupportedPluginVersion}");

            var pluginname = _fpath[(_fpath.LastIndexOf('/') + 1)..];
            var _path = "";

            var entries = archive.Entries.ToArray();
            foreach (var y in entries)
            {
                _path = y.Name.Length > 0
                    ? y.FullName.Replace(y.Name, "").Replace(_fpath, pluginname).TrimEnd('/')
                    : y.FullName.Replace(_fpath, pluginname);

                var entry = archive.CreateEntry($"{_path}/{y.Name}");
                using (var a = y.Open())
                using (var b = entry.Open())
                {
                    a.CopyTo(b);
                }

                archive.GetEntry(y.FullName).Delete();
            }
        }

        if (_pluginInfo == null)
            throw new Exception("No info file is found.");

        if (string.IsNullOrEmpty(uploadedItemDirectoryName))
            throw new Exception("Cannot get the plugin directory name");

        var pathToUpload = Path.Combine(pluginsPath, uploadedItemDirectoryName);

        try
        {
            DeleteDirectory(pathToUpload);
        }
        catch { }

        ZipFile.ExtractToDirectory(archivePath, pluginsPath);
    }

    #endregion
}