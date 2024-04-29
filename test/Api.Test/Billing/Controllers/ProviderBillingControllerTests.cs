﻿using Bit.Api.Billing.Controllers;
using Bit.Api.Billing.Models.Responses;
using Bit.Core;
using Bit.Core.Billing.Models;
using Bit.Core.Billing.Queries;
using Bit.Core.Context;
using Bit.Core.Enums;
using Bit.Core.Services;
using Bit.Core.Utilities;
using Bit.Test.Common.AutoFixture;
using Bit.Test.Common.AutoFixture.Attributes;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Stripe;
using Xunit;

namespace Bit.Api.Test.Billing.Controllers;

[ControllerCustomize(typeof(ProviderBillingController))]
[SutProviderCustomize]
public class ProviderBillingControllerTests
{
    [Theory, BitAutoData]
    public async Task GetSubscriptionAsync_FFDisabled_NotFound(
        Guid providerId,
        SutProvider<ProviderBillingController> sutProvider)
    {
        sutProvider.GetDependency<IFeatureService>().IsEnabled(FeatureFlagKeys.EnableConsolidatedBilling)
            .Returns(false);

        var result = await sutProvider.Sut.GetSubscriptionAsync(providerId);

        Assert.IsType<NotFound>(result);
    }

    [Theory, BitAutoData]
    public async Task GetSubscriptionAsync_NotProviderAdmin_Unauthorized(
        Guid providerId,
        SutProvider<ProviderBillingController> sutProvider)
    {
        sutProvider.GetDependency<IFeatureService>().IsEnabled(FeatureFlagKeys.EnableConsolidatedBilling)
            .Returns(true);

        sutProvider.GetDependency<ICurrentContext>().ProviderProviderAdmin(providerId)
            .Returns(false);

        var result = await sutProvider.Sut.GetSubscriptionAsync(providerId);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Theory, BitAutoData]
    public async Task GetSubscriptionAsync_NoSubscriptionData_NotFound(
        Guid providerId,
        SutProvider<ProviderBillingController> sutProvider)
    {
        sutProvider.GetDependency<IFeatureService>().IsEnabled(FeatureFlagKeys.EnableConsolidatedBilling)
            .Returns(true);

        sutProvider.GetDependency<ICurrentContext>().ProviderProviderAdmin(providerId)
            .Returns(true);

        sutProvider.GetDependency<IProviderBillingQueries>().GetSubscriptionDTO(providerId).ReturnsNull();

        var result = await sutProvider.Sut.GetSubscriptionAsync(providerId);

        Assert.IsType<NotFound>(result);
    }

    [Theory, BitAutoData]
    public async Task GetSubscriptionAsync_OK(
        Guid providerId,
        SutProvider<ProviderBillingController> sutProvider)
    {
        sutProvider.GetDependency<IFeatureService>().IsEnabled(FeatureFlagKeys.EnableConsolidatedBilling)
            .Returns(true);

        sutProvider.GetDependency<ICurrentContext>().ProviderProviderAdmin(providerId)
            .Returns(true);

        var configuredProviderPlanDTOList = new List<ConfiguredProviderPlanDTO>
        {
            new (Guid.NewGuid(), providerId, PlanType.TeamsMonthly, 50, 10, 30),
            new (Guid.NewGuid(), providerId, PlanType.EnterpriseMonthly, 100, 0, 90)
        };

        var subscription = new Subscription
        {
            Status = "active",
            CurrentPeriodEnd = new DateTime(2025, 1, 1),
            Customer = new Customer { Discount = new Discount { Coupon = new Coupon { PercentOff = 10 } } }
        };

        var providerSubscriptionDTO = new ProviderSubscriptionDTO(
            configuredProviderPlanDTOList,
            subscription);

        sutProvider.GetDependency<IProviderBillingQueries>().GetSubscriptionDTO(providerId)
            .Returns(providerSubscriptionDTO);

        var result = await sutProvider.Sut.GetSubscriptionAsync(providerId);

        Assert.IsType<Ok<ProviderSubscriptionResponse>>(result);

        var providerSubscriptionResponse = ((Ok<ProviderSubscriptionResponse>)result).Value;

        Assert.Equal(providerSubscriptionResponse.Status, subscription.Status);
        Assert.Equal(providerSubscriptionResponse.CurrentPeriodEndDate, subscription.CurrentPeriodEnd);
        Assert.Equal(providerSubscriptionResponse.DiscountPercentage, subscription.Customer!.Discount!.Coupon!.PercentOff);

        var teamsPlan = StaticStore.GetPlan(PlanType.TeamsMonthly);
        var providerTeamsPlan = providerSubscriptionResponse.Plans.FirstOrDefault(plan => plan.PlanName == teamsPlan.Name);
        Assert.NotNull(providerTeamsPlan);
        Assert.Equal(50, providerTeamsPlan.SeatMinimum);
        Assert.Equal(10, providerTeamsPlan.PurchasedSeats);
        Assert.Equal(30, providerTeamsPlan.AssignedSeats);
        Assert.Equal(60 * teamsPlan.PasswordManager.SeatPrice, providerTeamsPlan.Cost);
        Assert.Equal("Monthly", providerTeamsPlan.Cadence);

        var enterprisePlan = StaticStore.GetPlan(PlanType.EnterpriseMonthly);
        var providerEnterprisePlan = providerSubscriptionResponse.Plans.FirstOrDefault(plan => plan.PlanName == enterprisePlan.Name);
        Assert.NotNull(providerEnterprisePlan);
        Assert.Equal(100, providerEnterprisePlan.SeatMinimum);
        Assert.Equal(0, providerEnterprisePlan.PurchasedSeats);
        Assert.Equal(90, providerEnterprisePlan.AssignedSeats);
        Assert.Equal(100 * enterprisePlan.PasswordManager.SeatPrice, providerEnterprisePlan.Cost);
        Assert.Equal("Monthly", providerEnterprisePlan.Cadence);
    }
}
