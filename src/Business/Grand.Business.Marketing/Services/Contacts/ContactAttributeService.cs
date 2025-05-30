using Grand.Business.Core.Interfaces.Marketing.Contacts;
using Grand.Data;
using Grand.Domain.Customers;
using Grand.Domain.Messages;
using Grand.Infrastructure;
using Grand.Infrastructure.Caching;
using Grand.Infrastructure.Caching.Constants;
using Grand.Infrastructure.Configuration;
using Grand.Infrastructure.Extensions;
using MediatR;

namespace Grand.Business.Marketing.Services.Contacts;

/// <summary>
///     Contact attribute service
/// </summary>
public class ContactAttributeService : IContactAttributeService
{
    #region Ctor

    /// <summary>
    ///     Ctor
    /// </summary>
    public ContactAttributeService(ICacheBase cacheBase,
        IRepository<ContactAttribute> contactAttributeRepository,
        IMediator mediator,
        IContextAccessor contextAccessor, AccessControlConfig accessControlConfig)
    {
        _cacheBase = cacheBase;
        _contactAttributeRepository = contactAttributeRepository;
        _mediator = mediator;
        _contextAccessor = contextAccessor;
        _accessControlConfig = accessControlConfig;
    }

    #endregion

    #region Fields

    private readonly IRepository<ContactAttribute> _contactAttributeRepository;
    private readonly IMediator _mediator;
    private readonly ICacheBase _cacheBase;
    private readonly IContextAccessor _contextAccessor;
    private readonly AccessControlConfig _accessControlConfig;

    #endregion

    #region Methods

    #region Contact attributes

    /// <summary>
    ///     Deletes a contact attribute
    /// </summary>
    /// <param name="contactAttribute">Contact attribute</param>
    public virtual async Task DeleteContactAttribute(ContactAttribute contactAttribute)
    {
        ArgumentNullException.ThrowIfNull(contactAttribute);

        await _contactAttributeRepository.DeleteAsync(contactAttribute);

        await _cacheBase.RemoveByPrefix(CacheKey.CONTACTATTRIBUTES_PATTERN_KEY);
        await _cacheBase.RemoveByPrefix(CacheKey.CONTACTATTRIBUTEVALUES_PATTERN_KEY);

        //event notification
        await _mediator.EntityDeleted(contactAttribute);
    }

    /// <summary>
    ///     Gets all contact attributes
    /// </summary>
    /// <param name="storeId">Store identifier</param>
    /// <param name="ignoreAcl"></param>
    /// <returns>Contact attributes</returns>
    public virtual async Task<IList<ContactAttribute>> GetAllContactAttributes(string storeId = "",
        bool ignoreAcl = false)
    {
        var key = string.Format(CacheKey.CONTACTATTRIBUTES_ALL_KEY, storeId, ignoreAcl);
        return await _cacheBase.GetAsync(key, async () =>
        {
            var query = from p in _contactAttributeRepository.Table
                select p;

            query = query.OrderBy(c => c.DisplayOrder);

            if ((string.IsNullOrEmpty(storeId) || _accessControlConfig.IgnoreStoreLimitations) &&
                (ignoreAcl || _accessControlConfig.IgnoreAcl)) return await Task.FromResult(query.ToList());
            if (!ignoreAcl && !_accessControlConfig.IgnoreAcl)
            {
                var allowedCustomerGroupsIds = _contextAccessor.WorkContext.CurrentCustomer.GetCustomerGroupIds();
                query = from p in query
                    where !p.LimitedToGroups || allowedCustomerGroupsIds.Any(x => p.CustomerGroups.Contains(x))
                    select p;
            }

            //Store acl
            if (!string.IsNullOrEmpty(storeId) && !_accessControlConfig.IgnoreStoreLimitations)
                query = from p in query
                    where !p.LimitedToStores || p.Stores.Contains(storeId)
                    select p;
            return await Task.FromResult(query.ToList());
        });
    }

    /// <summary>
    ///     Gets a contact attribute
    /// </summary>
    /// <param name="contactAttributeId">Contact attribute identifier</param>
    /// <returns>Contact attribute</returns>
    public virtual Task<ContactAttribute> GetContactAttributeById(string contactAttributeId)
    {
        var key = string.Format(CacheKey.CONTACTATTRIBUTES_BY_ID_KEY, contactAttributeId);
        return _cacheBase.GetAsync(key, () => _contactAttributeRepository.GetByIdAsync(contactAttributeId));
    }

    /// <summary>
    ///     Inserts a contact attribute
    /// </summary>
    /// <param name="contactAttribute">Contact attribute</param>
    public virtual async Task InsertContactAttribute(ContactAttribute contactAttribute)
    {
        ArgumentNullException.ThrowIfNull(contactAttribute);

        await _contactAttributeRepository.InsertAsync(contactAttribute);

        await _cacheBase.RemoveByPrefix(CacheKey.CONTACTATTRIBUTES_PATTERN_KEY);
        await _cacheBase.RemoveByPrefix(CacheKey.CONTACTATTRIBUTEVALUES_PATTERN_KEY);

        //event notification
        await _mediator.EntityInserted(contactAttribute);
    }

    /// <summary>
    ///     Updates the contact attribute
    /// </summary>
    /// <param name="contactAttribute">Contact attribute</param>
    public virtual async Task UpdateContactAttribute(ContactAttribute contactAttribute)
    {
        ArgumentNullException.ThrowIfNull(contactAttribute);

        await _contactAttributeRepository.UpdateAsync(contactAttribute);

        await _cacheBase.RemoveByPrefix(CacheKey.CONTACTATTRIBUTES_PATTERN_KEY);
        await _cacheBase.RemoveByPrefix(CacheKey.CONTACTATTRIBUTEVALUES_PATTERN_KEY);

        //event notification
        await _mediator.EntityUpdated(contactAttribute);
    }

    #endregion

    #endregion
}