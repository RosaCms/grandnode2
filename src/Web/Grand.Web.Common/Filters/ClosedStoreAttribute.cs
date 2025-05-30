﻿using Grand.Business.Core.Interfaces.Cms;
using Grand.Business.Core.Interfaces.Common.Security;
using Grand.Domain.Permissions;
using Grand.Data;
using Grand.Domain.Stores;
using Grand.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Grand.Web.Common.Filters;

/// <summary>
///     Represents a filter attribute that confirms access to a closed store
/// </summary>
public class ClosedStoreAttribute : TypeFilterAttribute
{
    /// <summary>
    ///     Create instance of the filter attribute
    /// </summary>
    /// <param name="ignore">Whether to ignore the execution of filter actions</param>
    public ClosedStoreAttribute(bool ignore = false) : base(typeof(CheckAccessClosedStoreFilter))
    {
        IgnoreFilter = ignore;
        Arguments = [ignore];
    }

    public bool IgnoreFilter { get; }

    #region Filter

    /// <summary>
    ///     Represents a filter that confirms access to closed store
    /// </summary>
    private class CheckAccessClosedStoreFilter(bool ignoreFilter,
        IPermissionService permissionService,
        IContextAccessor contextAccessor,
        IPageService pageService,
        StoreInformationSettings storeInformationSettings) : IAsyncActionFilter
    {
        #region Methods

        /// <summary>
        ///     Called before the action executes, after model binding is complete
        /// </summary>
        /// <param name="context">A context for action filters</param>
        /// <param name="next">Action execution delegate</param>
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            //check whether this filter has been overridden for the Action
            var actionFilter = context.ActionDescriptor.FilterDescriptors
                .Where(f => f.Scope == FilterScope.Action)
                .Select(f => f.Filter).OfType<ClosedStoreAttribute>().FirstOrDefault();

            if (actionFilter?.IgnoreFilter ?? ignoreFilter)
            {
                await next();
                return;
            }

            if (!DataSettingsManager.DatabaseIsInstalled())
            {
                await next();
                return;
            }

            //store isn't closed
            if (!storeInformationSettings.StoreClosed)
            {
                await next();
                return;
            }

            //get action and controller names
            var actionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;
            var actionName = actionDescriptor?.ActionName;
            var controllerName = actionDescriptor?.ControllerName;

            if (string.IsNullOrEmpty(actionName) || string.IsNullOrEmpty(controllerName) ||
                (controllerName.Equals("Common", StringComparison.OrdinalIgnoreCase) &&
                 actionName.Equals("StoreClosed", StringComparison.OrdinalIgnoreCase)))
            {
                await next();
                return;
            }

            //pages accessible when a store is closed
            if (controllerName.Equals("Page", StringComparison.OrdinalIgnoreCase) &&
                actionName.Equals("PageDetails", StringComparison.OrdinalIgnoreCase))
            {
                //get identifiers of pages are accessible when a store is closed
                var now = DateTime.UtcNow;
                var allowedPageIds = (await pageService.GetAllPages(contextAccessor.StoreContext.CurrentStore.Id))
                    .Where(t => t.AccessibleWhenStoreClosed &&
                                (!t.StartDateUtc.HasValue || t.StartDateUtc < now) &&
                                (!t.EndDateUtc.HasValue || t.EndDateUtc > now))
                    .Select(page => page.Id);

                //check whether requested page is allowed
                var requestedPageId = context.RouteData.Values["pageId"] as string;
                if (!string.IsNullOrEmpty(requestedPageId) && allowedPageIds.Contains(requestedPageId))
                {
                    await next();
                    return;
                }
            }

            //check whether current customer has access to a closed store
            if (await permissionService.Authorize(StandardPermission.AccessClosedStore))
            {
                await next();
                return;
            }

            //store is closed and no access, so redirect to 'StoreClosed' page
            context.Result = new RedirectToRouteResult("StoreClosed", new RouteValueDictionary());
        }

        #endregion
    }

    #endregion
}