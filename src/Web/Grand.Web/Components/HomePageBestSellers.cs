﻿using Grand.Business.Core.Interfaces.Catalog.Products;
using Grand.Business.Core.Interfaces.System.Reports;
using Grand.Domain.Catalog;
using Grand.Domain.Payments;
using Grand.Infrastructure;
using Grand.Infrastructure.Caching;
using Grand.Web.Common.Components;
using Grand.Web.Events.Cache;
using Grand.Web.Features.Models.Products;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Grand.Web.Components;

public class HomePageBestSellersViewComponent : BaseViewComponent
{
    #region Constructors

    public HomePageBestSellersViewComponent(
        IOrderReportService orderReportService,
        ICacheBase cacheBase,
        IContextAccessor contextAccessor,
        IProductService productService,
        IMediator mediator,
        CatalogSettings catalogSettings)
    {
        _orderReportService = orderReportService;
        _cacheBase = cacheBase;
        _contextAccessor = contextAccessor;
        _productService = productService;
        _mediator = mediator;
        _catalogSettings = catalogSettings;
    }

    #endregion

    #region Invoker

    public async Task<IViewComponentResult> InvokeAsync(int? productThumbPictureSize)
    {
        if (!_catalogSettings.ShowBestsellersOnHomepage || _catalogSettings.NumberOfBestsellersOnHomepage == 0)
            return Content("");

        var productIds = new List<string>();

        //load and cache report
        if (_catalogSettings.BestsellersFromReports)
        {
            var fromdate = DateTime.UtcNow.AddMonths(_catalogSettings.PeriodBestsellers > 0
                ? -_catalogSettings.PeriodBestsellers
                : -12);
            var report = await _cacheBase.GetAsync(
                string.Format(CacheKeyConst.HOMEPAGE_BESTSELLERS_IDS_KEY, _contextAccessor.StoreContext.CurrentStore.Id), async () =>
                    await _orderReportService.BestSellersReport(
                        createdFromUtc: fromdate,
                        ps: PaymentStatus.Paid,
                        storeId: _contextAccessor.StoreContext.CurrentStore.Id,
                        pageSize: _catalogSettings.NumberOfBestsellersOnHomepage));

            productIds = report.Select(x => x.ProductId).ToList();
        }
        else
        {
            productIds = await _cacheBase.GetAsync(CacheKeyConst.BESTSELLER_PRODUCTS_MODEL_KEY,
                async () => (await _productService.GetAllProductsDisplayedOnBestSeller()).ToList());
        }

        //load products
        var products = await _productService.GetProductsByIds(productIds.ToArray());

        if (!products.Any())
            return Content("");

        var model = await _mediator.Send(new GetProductOverview {
            ProductThumbPictureSize = productThumbPictureSize,
            Products = products.Take(_catalogSettings.NumberOfBestsellersOnHomepage)
        });

        return View(model);
    }

    #endregion

    #region Fields

    private readonly IOrderReportService _orderReportService;
    private readonly ICacheBase _cacheBase;
    private readonly IContextAccessor _contextAccessor;
    private readonly IProductService _productService;
    private readonly IMediator _mediator;

    private readonly CatalogSettings _catalogSettings;

    #endregion
}