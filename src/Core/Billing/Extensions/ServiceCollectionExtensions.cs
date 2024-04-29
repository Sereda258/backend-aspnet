﻿using Bit.Core.Billing.Commands;
using Bit.Core.Billing.Commands.Implementations;
using Bit.Core.Billing.Queries;
using Bit.Core.Billing.Queries.Implementations;

namespace Bit.Core.Billing.Extensions;

using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static void AddBillingOperations(this IServiceCollection services)
    {
        // Queries
        services.AddTransient<IOrganizationBillingQueries, OrganizationBillingQueries>();
        services.AddTransient<IProviderBillingQueries, ProviderBillingQueries>();
        services.AddTransient<ISubscriberQueries, SubscriberQueries>();

        // Commands
        services.AddTransient<IAssignSeatsToClientOrganizationCommand, AssignSeatsToClientOrganizationCommand>();
        services.AddTransient<ICancelSubscriptionCommand, CancelSubscriptionCommand>();
        services.AddTransient<ICreateCustomerCommand, CreateCustomerCommand>();
        services.AddTransient<IRemovePaymentMethodCommand, RemovePaymentMethodCommand>();
        services.AddTransient<IScaleSeatsCommand, ScaleSeatsCommand>();
        services.AddTransient<IStartSubscriptionCommand, StartSubscriptionCommand>();
    }
}
