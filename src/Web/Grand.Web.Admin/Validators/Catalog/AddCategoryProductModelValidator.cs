﻿using Grand.Business.Core.Interfaces.Catalog.Categories;
using Grand.Business.Core.Interfaces.Common.Localization;
using Grand.Domain.Catalog;
using Grand.Infrastructure;
using Grand.Infrastructure.Validators;
using Grand.Web.Admin.Models.Catalog;

namespace Grand.Web.Admin.Validators.Catalog;

public class AddCategoryProductModelValidator : BaseStoreAccessValidator<CategoryModel.AddCategoryProductModel, Category>
{
    private readonly ICategoryService _categoryService;
    public AddCategoryProductModelValidator(
        IEnumerable<IValidatorConsumer<CategoryModel.AddCategoryProductModel>> validators,
        ITranslationService translationService, ICategoryService categoryService, IContextAccessor contextAccessor)
        : base(validators, translationService, contextAccessor)
    {
        _categoryService = categoryService;
    }
    protected override async Task<Category> GetEntity(CategoryModel.AddCategoryProductModel model)
    {
        return await _categoryService.GetCategoryById(model.CategoryId);
    }

    protected override string GetPermissionsResourceKey => "Admin.Catalog.Categories.Permissions";
}