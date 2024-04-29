﻿using Bit.Core.AdminConsole.Entities;
using Bit.Core.Billing.Constants;
using Bit.Core.Billing.Queries;
using Bit.Core.Billing.Queries.Implementations;
using Bit.Core.Repositories;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using NSubstitute;
using Stripe;
using Xunit;

namespace Bit.Core.Test.Billing.Queries;

[SutProviderCustomize]
public class OrganizationBillingQueriesTests
{
    #region GetMetadata
    [Theory, BitAutoData]
    public async Task GetMetadata_OrganizationNull_ReturnsNull(
        Guid organizationId,
        SutProvider<OrganizationBillingQueries> sutProvider)
    {
        var metadata = await sutProvider.Sut.GetMetadata(organizationId);

        Assert.Null(metadata);
    }

    [Theory, BitAutoData]
    public async Task GetMetadata_CustomerNull_ReturnsNull(
        Guid organizationId,
        Organization organization,
        SutProvider<OrganizationBillingQueries> sutProvider)
    {
        sutProvider.GetDependency<IOrganizationRepository>().GetByIdAsync(organizationId).Returns(organization);

        var metadata = await sutProvider.Sut.GetMetadata(organizationId);

        Assert.False(metadata.IsOnSecretsManagerStandalone);
    }

    [Theory, BitAutoData]
    public async Task GetMetadata_SubscriptionNull_ReturnsNull(
        Guid organizationId,
        Organization organization,
        SutProvider<OrganizationBillingQueries> sutProvider)
    {
        sutProvider.GetDependency<IOrganizationRepository>().GetByIdAsync(organizationId).Returns(organization);

        sutProvider.GetDependency<ISubscriberQueries>().GetCustomer(organization).Returns(new Customer());

        var metadata = await sutProvider.Sut.GetMetadata(organizationId);

        Assert.False(metadata.IsOnSecretsManagerStandalone);
    }

    [Theory, BitAutoData]
    public async Task GetMetadata_Succeeds(
        Guid organizationId,
        Organization organization,
        SutProvider<OrganizationBillingQueries> sutProvider)
    {
        sutProvider.GetDependency<IOrganizationRepository>().GetByIdAsync(organizationId).Returns(organization);

        var subscriberQueries = sutProvider.GetDependency<ISubscriberQueries>();

        subscriberQueries
            .GetCustomer(organization, Arg.Is<CustomerGetOptions>(options => options.Expand.FirstOrDefault() == "discount.coupon.applies_to"))
            .Returns(new Customer
            {
                Discount = new Discount
                {
                    Coupon = new Coupon
                    {
                        Id = StripeConstants.CouponIDs.SecretsManagerStandalone,
                        AppliesTo = new CouponAppliesTo
                        {
                            Products = ["product_id"]
                        }
                    }
                }
            });

        subscriberQueries.GetSubscription(organization).Returns(new Subscription
        {
            Items = new StripeList<SubscriptionItem>
            {
                Data =
                [
                    new SubscriptionItem
                    {
                        Plan = new Plan
                        {
                            ProductId = "product_id"
                        }
                    }
                ]
            }
        });

        var metadata = await sutProvider.Sut.GetMetadata(organizationId);

        Assert.True(metadata.IsOnSecretsManagerStandalone);
    }
    #endregion
}
