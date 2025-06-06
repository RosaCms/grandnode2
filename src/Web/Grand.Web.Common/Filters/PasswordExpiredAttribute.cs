﻿using Grand.Business.Core.Queries.Customers;
using Grand.Data;
using Grand.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Grand.Web.Common.Filters;

/// <summary>
///     Represents filter attribute that validates customer password expiration
/// </summary>
public class PasswordExpiredAttribute : TypeFilterAttribute
{
    /// <summary>
    ///     Create instance of the filter attribute
    /// </summary>
    public PasswordExpiredAttribute() : base(typeof(PasswordFilter))
    {
    }

    #region Filter

    /// <summary>
    ///     Represents a filter that validates customer password expiration
    /// </summary>
    private class PasswordFilter(IContextAccessor contextAccessor, IMediator mediator) : IAsyncAuthorizationFilter
    {
        #region Methods

        /// <summary>
        ///     Called before the action executes, after model binding is complete
        /// </summary>
        /// <param name="context">A context for action filters</param>
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (!DataSettingsManager.DatabaseIsInstalled())
                return;

            //get action and controller names
            var actionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;
            var actionName = actionDescriptor?.ActionName;
            var controllerName = actionDescriptor?.ControllerName;

            if (string.IsNullOrEmpty(actionName) || string.IsNullOrEmpty(controllerName))
                return;

            //don't validate on ChangePassword page and store closed
            if (!(controllerName.Equals("Account", StringComparison.OrdinalIgnoreCase) &&
                  actionName.Equals("ChangePassword", StringComparison.OrdinalIgnoreCase))
                &&
                !(controllerName.Equals("Common", StringComparison.OrdinalIgnoreCase) &&
                  actionName.Equals("StoreClosed", StringComparison.OrdinalIgnoreCase))
               )
            {
                //check password expiration
                var passwordIsExpired = await mediator.Send(new GetPasswordIsExpiredQuery
                    { Customer = contextAccessor.WorkContext.CurrentCustomer });
                if (passwordIsExpired)
                    //redirect to ChangePassword page if expires
                    context.Result = new RedirectToRouteResult("CustomerChangePassword", new RouteValueDictionary());
            }
        }

        #endregion
    }

    #endregion
}