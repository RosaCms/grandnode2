﻿using Grand.Business.Core.Enums.Checkout;
using Grand.Business.Core.Extensions;
using Grand.Business.Core.Interfaces.Catalog.Prices;
using Grand.Business.Core.Interfaces.Catalog.Products;
using Grand.Business.Core.Interfaces.Checkout.CheckoutAttributes;
using Grand.Business.Core.Interfaces.Checkout.Shipping;
using Grand.Business.Core.Interfaces.Common.Directory;
using Grand.Business.Core.Interfaces.Common.Localization;
using Grand.Business.Core.Utilities.Checkout;
using Grand.Domain.Catalog;
using Grand.Domain.Common;
using Grand.Domain.Customers;
using Grand.Domain.Orders;
using Grand.Domain.Shipping;
using Grand.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shipping.ByWeight.Services;

namespace Shipping.ByWeight;

public class ByWeightShippingCalcPlugin : IShippingRateCalculationProvider
{
    #region Ctor

    public ByWeightShippingCalcPlugin(
        IShippingMethodService shippingMethodService,
        IContextAccessor contextAccessor,
        ITranslationService translationService,
        IProductService productService,
        ICheckoutAttributeParser checkoutAttributeParser,
        ICurrencyService currencyService,
        IHttpContextAccessor httpContextAccessor,
        ByWeightShippingSettings byWeightShippingSettings)
    {
        _shippingMethodService = shippingMethodService;
        _contextAccessor = contextAccessor;
        _translationService = translationService;
        _productService = productService;
        _checkoutAttributeParser = checkoutAttributeParser;
        _currencyService = currencyService;
        _httpContextAccessor = httpContextAccessor;
        _byWeightShippingSettings = byWeightShippingSettings;
    }

    #endregion

    #region Utilities

    private async Task<double?> GetRate(double subTotal, double weight, string shippingMethodId,
        string storeId, string warehouseId, string countryId, string stateProvinceId, string zip)
    {
        var shippingByWeightService =
            _httpContextAccessor.HttpContext!.RequestServices.GetRequiredService<IShippingByWeightService>();

        var shippingByWeightRecord = await shippingByWeightService.FindRecord(shippingMethodId,
            storeId, warehouseId, countryId, stateProvinceId, zip, weight);
        if (shippingByWeightRecord == null)
        {
            if (_byWeightShippingSettings.LimitMethodsToCreated)
                return null;

            return 0;
        }

        //additional fixed cost
        var shippingTotal = shippingByWeightRecord.AdditionalFixedCost;
        //charge amount per weight unit
        if (shippingByWeightRecord.RatePerWeightUnit > 0)
        {
            var weightRate = weight - shippingByWeightRecord.LowerWeightLimit;
            if (weightRate < 0)
                weightRate = 0;
            shippingTotal += shippingByWeightRecord.RatePerWeightUnit * weightRate;
        }

        //percentage rate of subtotal
        if (shippingByWeightRecord.PercentageRateOfSubtotal > 0)
            shippingTotal += Math.Round((float)subTotal * (float)shippingByWeightRecord.PercentageRateOfSubtotal / 100f,
                2);

        if (shippingTotal < 0)
            shippingTotal = 0;
        return shippingTotal;
    }

    #endregion

    #region Fields

    private readonly IShippingMethodService _shippingMethodService;
    private readonly IContextAccessor _contextAccessor;
    private readonly ITranslationService _translationService;
    private readonly IProductService _productService;
    private readonly ICheckoutAttributeParser _checkoutAttributeParser;
    private readonly ICurrencyService _currencyService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ByWeightShippingSettings _byWeightShippingSettings;

    #endregion

    #region Methods

    /// <summary>
    ///     Gets shopping cart item weight (of one item)
    /// </summary>
    /// <param name="shoppingCartItem">Shopping cart item</param>
    /// <returns>Shopping cart item weight</returns>
    private async Task<double> GetShoppingCartItemWeight(ShoppingCartItem shoppingCartItem)
    {
        ArgumentNullException.ThrowIfNull(shoppingCartItem);

        var product = await _productService.GetProductById(shoppingCartItem.ProductId);
        if (product == null)
            return 0;

        //attribute weight
        double attributesTotalWeight = 0;
        if (shoppingCartItem.Attributes != null && shoppingCartItem.Attributes.Any())
        {
            var attributeValues = product.ParseProductAttributeValues(shoppingCartItem.Attributes);
            foreach (var attributeValue in attributeValues)
                switch (attributeValue.AttributeValueTypeId)
                {
                    case AttributeValueType.Simple:
                    {
                        //simple attribute
                        attributesTotalWeight += attributeValue.WeightAdjustment;
                    }
                        break;
                    case AttributeValueType.AssociatedToProduct:
                    {
                        //bundled product
                        var associatedProduct =
                            await _productService.GetProductById(attributeValue.AssociatedProductId);
                        if (associatedProduct is { IsShipEnabled: true })
                            attributesTotalWeight += associatedProduct.Weight * attributeValue.Quantity;
                    }
                        break;
                }
        }

        var weight = product.Weight + attributesTotalWeight;
        return weight;
    }

    /// <summary>
    ///     Gets shopping cart weight
    /// </summary>
    /// <param name="request">Request</param>
    /// <param name="includeCheckoutAttributes">
    ///     A value indicating whether we should calculate weights of selected checkout
    ///     attributes
    /// </param>
    /// <returns>Total weight</returns>
    private async Task<double> GetTotalWeight(GetShippingOptionRequest request, bool includeCheckoutAttributes = true)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customer = request.Customer;

        double totalWeight = 0;
        //shopping cart items
        foreach (var packageItem in request.Items)
            totalWeight += await GetShoppingCartItemWeight(packageItem.ShoppingCartItem) * packageItem.GetQuantity();

        //checkout attributes
        if (customer != null && includeCheckoutAttributes)
        {
            var checkoutAttributes =
                customer.GetUserFieldFromEntity<List<CustomAttribute>>(SystemCustomerFieldNames.CheckoutAttributes,
                    request.StoreId);
            if (checkoutAttributes.Any())
            {
                var attributeValues = await _checkoutAttributeParser.ParseCheckoutAttributeValues(checkoutAttributes);
                foreach (var attributeValue in attributeValues)
                    totalWeight += attributeValue.WeightAdjustment;
            }
        }

        return totalWeight;
    }


    /// <summary>
    ///     Gets available shipping options
    /// </summary>
    /// <param name="getShippingOptionRequest">A request for getting shipping options</param>
    /// <returns>Represents a response of getting shipping rate options</returns>
    public async Task<GetShippingOptionResponse> GetShippingOptions(GetShippingOptionRequest getShippingOptionRequest)
    {
        ArgumentNullException.ThrowIfNull(getShippingOptionRequest);

        var response = new GetShippingOptionResponse();

        if (getShippingOptionRequest.Items == null || getShippingOptionRequest.Items.Count == 0)
        {
            response.AddError("No shipment items");
            return response;
        }

        if (getShippingOptionRequest.ShippingAddress == null)
        {
            response.AddError("Shipping address is not set");
            return response;
        }

        var storeId = getShippingOptionRequest.StoreId;
        if (string.IsNullOrEmpty(storeId))
            storeId = _contextAccessor.StoreContext.CurrentStore.Id;
        var countryId = getShippingOptionRequest.ShippingAddress.CountryId;
        var stateProvinceId = getShippingOptionRequest.ShippingAddress.StateProvinceId;

        //string warehouseId = getShippingOptionRequest.WarehouseFrom != null ? getShippingOptionRequest.WarehouseFrom.Id : "";

        var zip = getShippingOptionRequest.ShippingAddress.ZipPostalCode;
        double subTotal = 0;
        var priceCalculationService =
            _httpContextAccessor.HttpContext!.RequestServices.GetRequiredService<IPricingService>();

        foreach (var packageItem in getShippingOptionRequest.Items)
        {
            if (packageItem.ShoppingCartItem.IsFreeShipping)
                continue;

            var product = await _productService.GetProductById(packageItem.ShoppingCartItem.ProductId);
            if (product != null)
                subTotal += (await priceCalculationService.GetSubTotal(packageItem.ShoppingCartItem, product)).subTotal;
        }

        var weight = await GetTotalWeight(getShippingOptionRequest);

        var shippingMethods =
            await _shippingMethodService.GetAllShippingMethods(countryId, _contextAccessor.WorkContext.CurrentCustomer);
        foreach (var shippingMethod in shippingMethods)
        {
            double? rate = null;
            foreach (var item in getShippingOptionRequest.Items.GroupBy(x => x.ShoppingCartItem.WarehouseId)
                         .Select(x => x.Key))
            {
                var _rate = await GetRate(subTotal, weight, shippingMethod.Id, storeId, item, countryId,
                    stateProvinceId, zip);
                if (_rate.HasValue)
                {
                    rate ??= 0;

                    rate += _rate.Value;
                }
            }

            if (rate is not null)
            {
                var shippingOption = new ShippingOption {
                    Name = shippingMethod.GetTranslation(x => x.Name, _contextAccessor.WorkContext.WorkingLanguage.Id),
                    Description = shippingMethod.GetTranslation(x => x.Description, _contextAccessor.WorkContext.WorkingLanguage.Id),
                    Rate = await _currencyService.ConvertFromPrimaryStoreCurrency(rate.Value,
                        _contextAccessor.WorkContext.WorkingCurrency)
                };
                response.ShippingOptions.Add(shippingOption);
            }
        }


        return response;
    }

    /// <summary>
    ///     Gets fixed shipping rate (if Shipping rate  method allows it and the rate can be calculated before checkout).
    /// </summary>
    /// <param name="getShippingOptionRequest">A request for getting shipping options</param>
    /// <returns>Fixed shipping rate; or null in case there's no fixed shipping rate</returns>
    public async Task<double?> GetFixedRate(GetShippingOptionRequest getShippingOptionRequest)
    {
        return await Task.FromResult(default(double?));
    }

    /// <summary>
    ///     Returns a value indicating whether shipping methods should be hidden during checkout
    /// </summary>
    /// <param name="cart">Shopping cart</param>
    /// <returns>true - hide; false - display.</returns>
    public async Task<bool> HideShipmentMethods(IList<ShoppingCartItem> cart)
    {
        //you can put any logic here
        //for example, hide this shipping methods if all products in the cart are downloadable
        //or hide this shipping methods if current customer is from certain country
        return await Task.FromResult(false);
    }

    #endregion

    #region Properties

    /// <summary>
    ///     Gets a shipment tracker
    /// </summary>
    public IShipmentTracker ShipmentTracker => null;

    public ShippingRateCalculationType ShippingRateCalculationType => ShippingRateCalculationType.Off;

    public string ConfigurationUrl => ByWeightShippingDefaults.ConfigurationUrl;

    public string SystemName => ByWeightShippingDefaults.ProviderSystemName;

    public string FriendlyName => _translationService.GetResource(ByWeightShippingDefaults.FriendlyName);

    public int Priority => _byWeightShippingSettings.DisplayOrder;

    public IList<string> LimitedToStores => new List<string>();

    public IList<string> LimitedToGroups => new List<string>();

    public async Task<IList<string>> ValidateShippingForm(string shippingOption, IDictionary<string, string> data)
    {
        //you can implement here any validation logic
        return await Task.FromResult(new List<string>());
    }

    public async Task<string> GetControllerRouteName()
    {
        return await Task.FromResult("");
    }

    #endregion
}