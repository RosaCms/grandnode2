﻿using Grand.Module.Api.DTOs.Catalog;
using MediatR;

namespace Grand.Module.Api.Commands.Models.Catalog;

public class AddProductTierPriceCommand : IRequest<bool>
{
    public ProductDto Product { get; set; }
    public ProductTierPriceDto Model { get; set; }
}