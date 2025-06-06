﻿using Grand.Business.Core.Extensions;
using Grand.Business.Core.Interfaces.Catalog.Categories;
using Grand.Business.Core.Interfaces.Catalog.Products;
using Grand.Business.Core.Interfaces.Common.Localization;
using Grand.Business.Core.Interfaces.Storage;
using Grand.Business.Core.Queries.Catalog;
using Grand.Domain;
using Grand.Domain.Catalog;
using Grand.Domain.Customers;
using Grand.Domain.Media;
using Grand.Infrastructure.Caching;
using Grand.Web.Events.Cache;
using Grand.Web.Extensions;
using Grand.Web.Features.Models.Catalog;
using Grand.Web.Features.Models.Products;
using Grand.Web.Models.Catalog;
using Grand.Web.Models.Media;
using MediatR;
using Microsoft.AspNetCore.Http.Extensions;

namespace Grand.Web.Features.Handlers.Catalog;

public class GetCategoryHandler : IRequestHandler<GetCategory, CategoryModel>
{
    private readonly ICacheBase _cacheBase;
    private readonly CatalogSettings _catalogSettings;
    private readonly ICategoryService _categoryService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly MediaSettings _mediaSettings;
    private readonly IMediator _mediator;
    private readonly IPictureService _pictureService;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly ITranslationService _translationService;

    public GetCategoryHandler(
        IMediator mediator,
        ICacheBase cacheBase,
        ICategoryService categoryService,
        IPictureService pictureService,
        ITranslationService translationService,
        ISpecificationAttributeService specificationAttributeService,
        IHttpContextAccessor httpContextAccessor,
        CatalogSettings catalogSettings,
        MediaSettings mediaSettings)
    {
        _mediator = mediator;
        _cacheBase = cacheBase;
        _categoryService = categoryService;
        _pictureService = pictureService;
        _translationService = translationService;
        _specificationAttributeService = specificationAttributeService;
        _httpContextAccessor = httpContextAccessor;
        _catalogSettings = catalogSettings;
        _mediaSettings = mediaSettings;
    }

    public async Task<CategoryModel> Handle(GetCategory request, CancellationToken cancellationToken)
    {
        var model = request.Category.ToModel(request.Language);
        var customer = request.Customer;
        var storeId = request.Store.Id;
        var languageId = request.Language.Id;

        if (request.Command is { OrderBy: null } && request.Category.DefaultSort >= 0)
            request.Command.OrderBy = request.Category.DefaultSort;

        //view/sorting/page size
        var options = await _mediator.Send(new GetViewSortSizeOptions {
            Command = request.Command,
            PagingFilteringModel = request.Command,
            Language = request.Language,
            AllowCustomersToSelectPageSize = request.Category.AllowCustomersToSelectPageSize,
            PageSizeOptions = request.Category.PageSizeOptions,
            PageSize = request.Category.PageSize
        }, cancellationToken);
        model.PagingFilteringContext = options.command;

        //price ranges
        //category breadcrumb
        if (_catalogSettings.CategoryBreadcrumbEnabled)
        {
            model.DisplayCategoryBreadcrumb = true;

            var breadcrumbCacheKey = string.Format(CacheKeyConst.CATEGORY_BREADCRUMB_KEY,
                request.Category.Id,
                string.Join(",", customer.GetCustomerGroupIds()),
                storeId,
                languageId);
            model.CategoryBreadcrumb = await _cacheBase.GetAsync(breadcrumbCacheKey, async () =>
                (await _categoryService.GetCategoryBreadCrumb(request.Category))
                .Select(catBr => new CategoryModel {
                    Id = catBr.Id,
                    Name = catBr.GetTranslation(x => x.Name, languageId),
                    SeName = catBr.GetSeName(languageId)
                })
                .ToList()
            );
        }

        //subcategories
        var subCategories = new List<CategoryModel.SubCategoryModel>();
        foreach (var x in (await _categoryService.GetAllCategoriesByParentCategoryId(request.Category.Id)).Where(x =>
                     !x.HideOnCatalog))
        {
            var subCatModel = new CategoryModel.SubCategoryModel {
                Id = x.Id,
                Name = x.GetTranslation(y => y.Name, languageId),
                SeName = x.GetSeName(languageId),
                Description = x.GetTranslation(y => y.Description, languageId),
                Flag = x.Flag,
                FlagStyle = x.FlagStyle
            };
            //prepare picture model
            var picture = !string.IsNullOrEmpty(x.PictureId) ? await _pictureService.GetPictureById(x.PictureId) : null;
            subCatModel.PictureModel = new PictureModel {
                Id = x.PictureId,
                FullSizeImageUrl = await _pictureService.GetPictureUrl(x.PictureId),
                ImageUrl = await _pictureService.GetPictureUrl(x.PictureId, _mediaSettings.CategoryThumbPictureSize),
                Style = picture?.Style,
                ExtraField = picture?.ExtraField,
                //"title" attribute
                Title =
                    picture != null &&
                    !string.IsNullOrEmpty(picture.GetTranslation(z => z.TitleAttribute, request.Language.Id))
                        ? picture.GetTranslation(z => z.TitleAttribute, request.Language.Id)
                        : string.Format(_translationService.GetResource("Media.Category.ImageLinkTitleFormat"), x.Name),
                //"alt" attribute
                AlternateText =
                    picture != null &&
                    !string.IsNullOrEmpty(picture.GetTranslation(z => z.AltAttribute, request.Language.Id))
                        ? picture.GetTranslation(z => z.AltAttribute, request.Language.Id)
                        : string.Format(_translationService.GetResource("Media.Category.ImageAlternateTextFormat"),
                            x.Name)
            };

            subCategories.Add(subCatModel);
        }

        model.SubCategories = subCategories;

        //featured products
        if (!_catalogSettings.IgnoreFeaturedProducts)
        {
            //We cache a value indicating whether we have featured products
            IPagedList<Product> featuredProducts = null;
            var cacheKey = string.Format(CacheKeyConst.CATEGORY_HAS_FEATURED_PRODUCTS_KEY, request.Category.Id,
                string.Join(",", customer.GetCustomerGroupIds()), storeId);

            var hasFeaturedProductsCache = await _cacheBase.GetAsync<bool?>(cacheKey, async () =>
            {
                featuredProducts = (await _mediator.Send(new GetSearchProductsQuery {
                    PageSize = _catalogSettings.LimitOfFeaturedProducts,
                    CategoryIds = new List<string> { request.Category.Id },
                    Customer = request.Customer,
                    StoreId = storeId,
                    VisibleIndividuallyOnly = true,
                    FeaturedProducts = true
                }, cancellationToken)).products;
                return featuredProducts.Any();
            });

            if (hasFeaturedProductsCache.HasValue && hasFeaturedProductsCache.Value && featuredProducts == null)
                //cache indicates that the category has featured products
                featuredProducts = (await _mediator.Send(new GetSearchProductsQuery {
                    PageSize = _catalogSettings.LimitOfFeaturedProducts,
                    CategoryIds = new List<string> { request.Category.Id },
                    Customer = request.Customer,
                    StoreId = storeId,
                    VisibleIndividuallyOnly = true,
                    FeaturedProducts = true
                }, cancellationToken)).products;
            if (featuredProducts != null && featuredProducts.Any())
                model.FeaturedProducts = (await _mediator.Send(new GetProductOverview {
                    Products = featuredProducts
                }, cancellationToken)).ToList();
        }


        var categoryIds = new List<string> { request.Category.Id };
        if (_catalogSettings.ShowProductsFromSubcategories)
            //include subcategories
            categoryIds.AddRange(await _mediator.Send(
                new GetChildCategoryIds
                    { ParentCategoryId = request.Category.Id, Customer = request.Customer, Store = request.Store },
                cancellationToken));
        //products
        IList<string> alreadyFilteredSpecOptionIds =
            await model.PagingFilteringContext.SpecificationFilter.GetAlreadyFilteredSpecOptionIds(
                _httpContextAccessor.HttpContext?.Request.Query, _specificationAttributeService);
        var products = await _mediator.Send(new GetSearchProductsQuery {
            LoadFilterableSpecificationAttributeOptionIds = !_catalogSettings.IgnoreFilterableSpecAttributeOption,
            CategoryIds = categoryIds,
            Customer = request.Customer,
            StoreId = storeId,
            VisibleIndividuallyOnly = true,
            FeaturedProducts = _catalogSettings.IncludeFeaturedProductsInNormalLists ? null : false,
            FilteredSpecs = alreadyFilteredSpecOptionIds,
            OrderBy = (ProductSortingEnum)request.Command.OrderBy!,
            Rating = request.Command.Rating,
            PageIndex = request.Command.PageNumber - 1,
            PageSize = request.Command.PageSize
        }, cancellationToken);

        model.Products = (await _mediator.Send(new GetProductOverview {
            PrepareSpecificationAttributes = _catalogSettings.ShowSpecAttributeOnCatalogPages,
            Products = products.products
        }, cancellationToken)).ToList();

        model.PagingFilteringContext.LoadPagedList(products.products);

        //specs
        await model.PagingFilteringContext.SpecificationFilter.PrepareSpecsFilters(alreadyFilteredSpecOptionIds,
            products.filterableSpecificationAttributeOptionIds,
            _specificationAttributeService, _httpContextAccessor.HttpContext?.Request.GetDisplayUrl(),
            request.Language.Id);

        return model;
    }
}