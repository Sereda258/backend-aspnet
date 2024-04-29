﻿using Bit.Core.Exceptions;
using Bit.Core.Models.Data;

namespace Bit.Api.AdminConsole.Public.Models.Request;

public class AssociationWithPermissionsRequestModel : AssociationWithPermissionsBaseModel
{
    public CollectionAccessSelection ToCollectionAccessSelection(bool migratedToFlexibleCollections)
    {
        var collectionAccessSelection = new CollectionAccessSelection
        {
            Id = Id.Value,
            ReadOnly = ReadOnly.Value,
            HidePasswords = HidePasswords.GetValueOrDefault(),
            Manage = Manage.GetValueOrDefault()
        };

        // Throws if the org has not migrated to use FC but has passed in a Manage value in the request
        if (!migratedToFlexibleCollections && Manage.GetValueOrDefault())
        {
            throw new BadRequestException(
                "Your organization must be using the latest collection enhancements to use the Manage property.");
        }

        return collectionAccessSelection;
    }
}
