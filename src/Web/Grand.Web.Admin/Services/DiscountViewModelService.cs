﻿using Grand.Business.Core.Interfaces.Catalog.Brands;
using Grand.Business.Core.Interfaces.Catalog.Categories;
using Grand.Business.Core.Interfaces.Catalog.Collections;
using Grand.Business.Core.Interfaces.Catalog.Discounts;
using Grand.Business.Core.Interfaces.Catalog.Prices;
using Grand.Business.Core.Interfaces.Catalog.Products;
using Grand.Business.Core.Interfaces.Checkout.Orders;
using Grand.Business.Core.Interfaces.Common.Directory;
using Grand.Business.Core.Interfaces.Common.Localization;
using Grand.Business.Core.Interfaces.Common.Stores;
using Grand.Business.Core.Interfaces.Customers;
using Grand.Business.Core.Queries.Catalog;
using Grand.Domain.Catalog;
using Grand.Domain.Discounts;
using Grand.Domain.Vendors;
using Grand.Infrastructure;
using Grand.Web.Admin.Extensions.Mapping;
using Grand.Web.Admin.Interfaces;
using Grand.Web.Admin.Models.Catalog;
using Grand.Web.Admin.Models.Discounts;
using Grand.Web.Common.Extensions;
using Grand.Web.Common.Localization;
using MediatR;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Grand.Web.Admin.Services;

public class DiscountViewModelService : IDiscountViewModelService
{
    #region Constructors

    public DiscountViewModelService(IDiscountService discountService,
        ITranslationService translationService,
        ICurrencyService currencyService,
        ICategoryService categoryService,
        IProductService productService,
        IContextAccessor contextAccessor,
        ICollectionService collectionService,
        IBrandService brandService,
        IStoreService storeService,
        IVendorService vendorService,
        IOrderService orderService,
        IPriceFormatter priceFormatter,
        IDateTimeService dateTimeService,
        IDiscountProviderLoader discountProviderLoader,
        IMediator mediator, 
        IEnumTranslationService enumTranslationService)
    {
        _discountService = discountService;
        _translationService = translationService;
        _currencyService = currencyService;
        _categoryService = categoryService;
        _productService = productService;
        _contextAccessor = contextAccessor;
        _collectionService = collectionService;
        _brandService = brandService;
        _storeService = storeService;
        _vendorService = vendorService;
        _orderService = orderService;
        _priceFormatter = priceFormatter;
        _dateTimeService = dateTimeService;
        _discountProviderLoader = discountProviderLoader;
        _mediator = mediator;
        _enumTranslationService = enumTranslationService;
    }

    #endregion

    #region Fields

    private readonly IDiscountService _discountService;
    private readonly ITranslationService _translationService;
    private readonly ICurrencyService _currencyService;
    private readonly ICategoryService _categoryService;
    private readonly IProductService _productService;
    private readonly IContextAccessor _contextAccessor;
    private readonly IBrandService _brandService;
    private readonly ICollectionService _collectionService;
    private readonly IStoreService _storeService;
    private readonly IVendorService _vendorService;
    private readonly IOrderService _orderService;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IDateTimeService _dateTimeService;
    private readonly IDiscountProviderLoader _discountProviderLoader;
    private readonly IMediator _mediator;
    private readonly IEnumTranslationService _enumTranslationService;

    #endregion

    public virtual DiscountListModel PrepareDiscountListModel()
    {
        var model = new DiscountListModel {
            AvailableDiscountTypes = _enumTranslationService.ToSelectList(DiscountType.AssignedToOrderTotal, false).ToList()
        };
        model.AvailableDiscountTypes.Insert(0,
            new SelectListItem { Text = _translationService.GetResource("Admin.Common.All"), Value = "" });
        return model;
    }

    public virtual async Task<(IEnumerable<DiscountModel> discountModel, int totalCount)> PrepareDiscountModel(
        DiscountListModel model, int pageIndex, int pageSize)
    {
        DiscountType? discountType = null;
        if (model.SearchDiscountTypeId > 0)
            discountType = (DiscountType)model.SearchDiscountTypeId;
        var discounts = await _discountService.GetDiscountsQuery(discountType,
            _contextAccessor.WorkContext.CurrentCustomer.StaffStoreId,
            null,
            model.SearchDiscountCouponCode,
            model.SearchDiscountName);
        var items = new List<DiscountModel>();
        foreach (var x in discounts.Skip((pageIndex - 1) * pageSize).Take(pageSize))
        {
            var discountModel = x.ToModel(_dateTimeService);
            discountModel.DiscountTypeName = _enumTranslationService.GetTranslationEnum(x.DiscountTypeId);
            discountModel.TimesUsed =
                (await _mediator.Send(new GetDiscountUsageHistoryQuery { DiscountId = x.Id, PageSize = 1 })).TotalCount;
            items.Add(discountModel);
        }

        return (items, discounts.Count);
    }

    public virtual async Task PrepareDiscountModel(DiscountModel model, Discount discount)
    {
        ArgumentNullException.ThrowIfNull(model);

        model.AvailableDiscountRequirementRules.Add(new SelectListItem {
            Text = _translationService.GetResource(
                "admin.marketing.discounts.Requirements.DiscountRequirementType.Select"),
            Value = ""
        });
        var discountPlugins = _discountProviderLoader.LoadAllDiscountProviders();
        foreach (var discountPlugin in discountPlugins)
        foreach (var discountRule in discountPlugin.GetRequirementRules())
            model.AvailableDiscountRequirementRules.Add(new SelectListItem
                { Text = discountRule.FriendlyName, Value = discountRule.SystemName });
        var currencies = await _currencyService.GetAllCurrencies();
        foreach (var item in currencies)
            model.AvailableCurrencies.Add(new SelectListItem { Text = item.Name, Value = item.CurrencyCode });

        //discount amount providers
        foreach (var item in _discountProviderLoader.LoadDiscountAmountProviders())
            model.AvailableDiscountAmountProviders.Add(new SelectListItem
                { Value = item.SystemName, Text = item.FriendlyName });

        if (discount != null)
            //requirements
            foreach (var dr in discount.DiscountRules.OrderBy(dr => dr.Id))
            {
                var discountPlugin =
                    _discountProviderLoader.LoadDiscountProviderByRuleSystemName(dr.DiscountRequirementRuleSystemName);
                var discountRequirement = discountPlugin.GetRequirementRules()
                    .Single(x => x.SystemName == dr.DiscountRequirementRuleSystemName);
                {
                    model.DiscountRequirementMetaInfos.Add(new DiscountModel.DiscountRequirementMetaInfo {
                        DiscountRequirementId = dr.Id,
                        RuleName = discountRequirement.FriendlyName,
                        ConfigurationUrl = GetRequirementUrlInternal(discountRequirement, discount, dr.Id)
                    });
                }
            }
        else
            model.IsEnabled = true;
    }

    public virtual async Task<Discount> InsertDiscountModel(DiscountModel model)
    {
        var discount = model.ToEntity(_dateTimeService);
        await _discountService.InsertDiscount(discount);

        return discount;
    }

    public virtual async Task<Discount> UpdateDiscountModel(Discount discount, DiscountModel model)
    {
        var prevDiscountType = discount.DiscountTypeId;
        discount = model.ToEntity(discount, _dateTimeService);
        await _discountService.UpdateDiscount(discount);

        switch (prevDiscountType)
        {
            //clean up old references (if changed) and update "HasDiscountsApplied" properties
            case DiscountType.AssignedToCategories
                when discount.DiscountTypeId != DiscountType.AssignedToCategories:
            {
                //applied to categories
                //_categoryService.
                var categories = await _categoryService.GetAllCategoriesByDiscount(discount.Id);

                //update "HasDiscountsApplied" property
                foreach (var category in categories)
                {
                    var item = category.AppliedDiscounts.FirstOrDefault(x => x == discount.Id);
                    category.AppliedDiscounts.Remove(item);
                }

                break;
            }
            case DiscountType.AssignedToCollections
                when discount.DiscountTypeId != DiscountType.AssignedToCollections:
            {
                //applied to collections
                var collections = await _collectionService.GetAllCollectionsByDiscount(discount.Id);
                foreach (var collection in collections)
                {
                    var item = collection.AppliedDiscounts.FirstOrDefault(x => x == discount.Id);
                    collection.AppliedDiscounts.Remove(item);
                }

                break;
            }
            case DiscountType.AssignedToSkus
                when discount.DiscountTypeId != DiscountType.AssignedToSkus:
            {
                //applied to products
                var products = await _productService.GetProductsByDiscount(discount.Id);

                foreach (var p in products)
                {
                    var item = p.AppliedDiscounts.FirstOrDefault(x => x == discount.Id);
                    p.AppliedDiscounts.Remove(item);
                    await _productService.DeleteDiscount(item, p.Id);
                }

                break;
            }
        }

        return discount;
    }

    public virtual async Task DeleteDiscount(Discount discount)
    {
        await _discountService.DeleteDiscount(discount);
    }

    public virtual async Task InsertCouponCode(string discountId, string couponCode)
    {
        var coupon = new DiscountCoupon {
            CouponCode = couponCode.ToUpper(),
            DiscountId = discountId
        };
        await _discountService.InsertDiscountCoupon(coupon);
    }

    public virtual string GetRequirementUrlInternal(IDiscountRule discountRequirementRule, Discount discount,
        string discountRequirementId)
    {
        ArgumentNullException.ThrowIfNull(discountRequirementRule);
        ArgumentNullException.ThrowIfNull(discount);

        var storeLocation = _contextAccessor.StoreContext.CurrentHost.Url.TrimEnd('/');

        var url =
            $"{storeLocation}/{discountRequirementRule.GetConfigurationUrl(discount.Id, discountRequirementId)}";
        return url;
    }

    public virtual async Task DeleteDiscountRequirement(DiscountRule discountRequirement, Discount discount)
    {
        discount.DiscountRules.Remove(discountRequirement);
        await _discountService.UpdateDiscount(discount);
    }

    public virtual async Task<DiscountModel.AddProductToDiscountModel> PrepareProductToDiscountModel()
    {
        var model = new DiscountModel.AddProductToDiscountModel();
        //stores
        model.AvailableStores.Add(new SelectListItem
            { Text = _translationService.GetResource("Admin.Common.All"), Value = " " });
        foreach (var s in await _storeService.GetAllStores())
            model.AvailableStores.Add(new SelectListItem { Text = s.Shortcut, Value = s.Id });

        //vendors
        model.AvailableVendors.Add(new SelectListItem
            { Text = _translationService.GetResource("Admin.Common.All"), Value = " " });
        foreach (var v in await _vendorService.GetAllVendors(showHidden: true))
            model.AvailableVendors.Add(new SelectListItem { Text = v.Name, Value = v.Id });

        //product types
        model.AvailableProductTypes = _enumTranslationService.ToSelectList(ProductType.SimpleProduct).ToList();
        model.AvailableProductTypes.Insert(0,
            new SelectListItem { Text = _translationService.GetResource("Admin.Common.All"), Value = " " });
        return model;
    }

    public virtual async Task<(IList<ProductModel> products, int totalCount)> PrepareProductModel(
        DiscountModel.AddProductToDiscountModel model, int pageIndex, int pageSize)
    {
        var products = await _productService.PrepareProductList(model.SearchCategoryId, model.SearchBrandId,
            model.SearchCollectionId, model.SearchStoreId, model.SearchVendorId, model.SearchProductTypeId,
            model.SearchProductName, pageIndex, pageSize);
        return (products.Select(x => x.ToModel(_dateTimeService)).ToList(), products.TotalCount);
    }

    public virtual async Task InsertProductToDiscountModel(DiscountModel.AddProductToDiscountModel model)
    {
        foreach (var id in model.SelectedProductIds)
        {
            var product = await _productService.GetProductById(id);
            if (product != null)
                if (product.AppliedDiscounts.Count(d => d == model.DiscountId) == 0)
                {
                    product.AppliedDiscounts.Add(model.DiscountId);
                    await _productService.InsertDiscount(model.DiscountId, product.Id);
                }
        }
    }

    public virtual async Task DeleteProduct(Discount discount, Product product)
    {
        //remove discount
        if (product.AppliedDiscounts.Count(d => d == discount.Id) > 0)
        {
            product.AppliedDiscounts.Remove(discount.Id);
            await _productService.DeleteDiscount(discount.Id, product.Id);
        }
    }

    public virtual async Task DeleteCategory(Discount discount, Category category)
    {
        //remove discount
        if (category.AppliedDiscounts.Count(d => d == discount.Id) > 0)
            category.AppliedDiscounts.Remove(discount.Id);

        await _categoryService.UpdateCategory(category);
    }

    public virtual async Task InsertCategoryToDiscountModel(DiscountModel.AddCategoryToDiscountModel model)
    {
        foreach (var id in model.SelectedCategoryIds)
        {
            var category = await _categoryService.GetCategoryById(id);
            if (category != null)
            {
                if (category.AppliedDiscounts.Count(d => d == model.DiscountId) == 0)
                    category.AppliedDiscounts.Add(model.DiscountId);

                await _categoryService.UpdateCategory(category);
            }
        }
    }

    public virtual async Task DeleteCollection(Discount discount, Collection collection)
    {
        //remove discount
        if (collection.AppliedDiscounts.Count(d => d == discount.Id) > 0)
            collection.AppliedDiscounts.Remove(discount.Id);

        await _collectionService.UpdateCollection(collection);
    }

    public virtual async Task InsertBrandToDiscountModel(DiscountModel.AddBrandToDiscountModel model)
    {
        foreach (var id in model.SelectedBrandIds)
        {
            var brand = await _brandService.GetBrandById(id);
            if (brand != null)
            {
                if (brand.AppliedDiscounts.Count(d => d == model.DiscountId) == 0)
                    brand.AppliedDiscounts.Add(model.DiscountId);

                await _brandService.UpdateBrand(brand);
            }
        }
    }

    public virtual async Task DeleteBrand(Discount discount, Brand brand)
    {
        //remove discount
        if (brand.AppliedDiscounts.Count(d => d == discount.Id) > 0)
            brand.AppliedDiscounts.Remove(discount.Id);

        await _brandService.UpdateBrand(brand);
    }

    public virtual async Task InsertCollectionToDiscountModel(DiscountModel.AddCollectionToDiscountModel model)
    {
        foreach (var id in model.SelectedCollectionIds)
        {
            var collection = await _collectionService.GetCollectionById(id);
            if (collection != null)
            {
                if (collection.AppliedDiscounts.Count(d => d == model.DiscountId) == 0)
                    collection.AppliedDiscounts.Add(model.DiscountId);

                await _collectionService.UpdateCollection(collection);
            }
        }
    }

    public virtual async Task DeleteVendor(Discount discount, Vendor vendor)
    {
        //remove discount
        if (vendor.AppliedDiscounts.Count(d => d == discount.Id) > 0)
            vendor.AppliedDiscounts.Remove(discount.Id);

        await _vendorService.UpdateVendor(vendor);
    }

    public virtual async Task InsertVendorToDiscountModel(DiscountModel.AddVendorToDiscountModel model)
    {
        foreach (var id in model.SelectedVendorIds)
        {
            var vendor = await _vendorService.GetVendorById(id);
            if (vendor != null)
            {
                if (vendor.AppliedDiscounts.Count(d => d == model.DiscountId) == 0)
                    vendor.AppliedDiscounts.Add(model.DiscountId);

                await _vendorService.UpdateVendor(vendor);
            }
        }
    }

    public virtual async
        Task<(IEnumerable<DiscountModel.DiscountUsageHistoryModel> usageHistoryModels, int totalCount)>
        PrepareDiscountUsageHistoryModel(Discount discount, int pageIndex, int pageSize)
    {
        var duh = await _mediator.Send(new GetDiscountUsageHistoryQuery
            { DiscountId = discount.Id, PageIndex = pageIndex - 1, PageSize = pageSize });
        var items = new List<DiscountModel.DiscountUsageHistoryModel>();

        foreach (var x in duh)
        {
            var order = await _orderService.GetOrderById(x.OrderId);
            var duhModel = new DiscountModel.DiscountUsageHistoryModel {
                Id = x.Id,
                DiscountId = x.DiscountId,
                OrderId = x.OrderId,
                OrderNumber = order?.OrderNumber ?? 0,
                OrderCode = order != null ? order.Code : "",
                OrderTotal = order != null
                    ? _priceFormatter.FormatPrice(order.OrderTotal, await _currencyService.GetPrimaryStoreCurrency())
                    : "",
                CreatedOn = _dateTimeService.ConvertToUserTime(x.CreatedOnUtc, DateTimeKind.Utc)
            };
            items.Add(duhModel);
        }

        return (items, duh.TotalCount);
    }
}