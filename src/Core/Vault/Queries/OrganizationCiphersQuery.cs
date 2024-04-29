﻿using Bit.Core.Exceptions;
using Bit.Core.Repositories;
using Bit.Core.Services;
using Bit.Core.Vault.Models.Data;
using Bit.Core.Vault.Repositories;

namespace Bit.Core.Vault.Queries;

public class OrganizationCiphersQuery : IOrganizationCiphersQuery
{
    private readonly ICipherRepository _cipherRepository;
    private readonly ICollectionCipherRepository _collectionCipherRepository;
    private readonly IFeatureService _featureService;

    private bool FlexibleCollectionsV1Enabled => _featureService.IsEnabled(FeatureFlagKeys.FlexibleCollectionsV1);

    public OrganizationCiphersQuery(ICipherRepository cipherRepository, ICollectionCipherRepository collectionCipherRepository, IFeatureService featureService)
    {
        _cipherRepository = cipherRepository;
        _collectionCipherRepository = collectionCipherRepository;
        _featureService = featureService;
    }

    /// <summary>
    /// Returns ciphers belonging to the organization that the user has been assigned to via collections.
    /// </summary>
    public async Task<IEnumerable<CipherDetailsWithCollections>> GetOrganizationCiphersForUser(Guid organizationId, Guid userId)
    {
        if (!FlexibleCollectionsV1Enabled)
        {
            // Flexible collections is OFF, should not be using this query
            throw new FeatureUnavailableException("Flexible collections is OFF when it should be ON.");
        }

        var ciphers = await _cipherRepository.GetManyByUserIdAsync(userId, useFlexibleCollections: true, withOrganizations: true);
        var orgCiphers = ciphers.Where(c => c.OrganizationId == organizationId).ToList();
        var orgCipherIds = orgCiphers.Select(c => c.Id);

        var collectionCiphers = await _collectionCipherRepository.GetManyByOrganizationIdAsync(organizationId);
        var collectionCiphersGroupDict = collectionCiphers
            .Where(c => orgCipherIds.Contains(c.CipherId))
            .GroupBy(c => c.CipherId).ToDictionary(s => s.Key);

        return orgCiphers.Select(c => new CipherDetailsWithCollections(c, collectionCiphersGroupDict));
    }

    /// <summary>
    /// Returns all ciphers belonging to the organization.
    /// </summary>
    /// <param name="organizationId"></param>
    public async Task<IEnumerable<CipherOrganizationDetailsWithCollections>> GetAllOrganizationCiphers(Guid organizationId)
    {
        if (!FlexibleCollectionsV1Enabled)
        {
            // Flexible collections is OFF, should not be using this query
            throw new FeatureUnavailableException("Flexible collections is OFF when it should be ON.");
        }

        var orgCiphers = await _cipherRepository.GetManyOrganizationDetailsByOrganizationIdAsync(organizationId);
        var collectionCiphers = await _collectionCipherRepository.GetManyByOrganizationIdAsync(organizationId);
        var collectionCiphersGroupDict = collectionCiphers.GroupBy(c => c.CipherId).ToDictionary(s => s.Key);

        return orgCiphers.Select(c => new CipherOrganizationDetailsWithCollections(c, collectionCiphersGroupDict));
    }

    /// <summary>
    /// Returns ciphers belonging to the organization that are not assigned to any collection.
    /// </summary>
    public async Task<IEnumerable<CipherOrganizationDetails>> GetUnassignedOrganizationCiphers(Guid organizationId)
    {
        if (!FlexibleCollectionsV1Enabled)
        {
            // Flexible collections is OFF, should not be using this query
            throw new FeatureUnavailableException("Flexible collections is OFF when it should be ON.");
        }

        return await _cipherRepository.GetManyUnassignedOrganizationDetailsByOrganizationIdAsync(organizationId);
    }
}
