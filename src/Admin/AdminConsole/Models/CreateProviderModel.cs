﻿using System.ComponentModel.DataAnnotations;
using Bit.Core.AdminConsole.Entities.Provider;
using Bit.Core.AdminConsole.Enums.Provider;
using Bit.SharedWeb.Utilities;

namespace Bit.Admin.AdminConsole.Models;

public class CreateProviderModel : IValidatableObject
{
    public CreateProviderModel() { }

    [Display(Name = "Provider Type")]
    public ProviderType Type { get; set; }

    [Display(Name = "Owner Email")]
    public string OwnerEmail { get; set; }

    [Display(Name = "Name")]
    public string Name { get; set; }

    [Display(Name = "Business Name")]
    public string BusinessName { get; set; }

    [Display(Name = "Primary Billing Email")]
    public string BillingEmail { get; set; }

    [Display(Name = "Teams minimum seats")]
    public int TeamsMinimumSeats { get; set; }

    [Display(Name = "Enterprise minimum seats")]
    public int EnterpriseMinimumSeats { get; set; }

    public virtual Provider ToProvider()
    {
        return new Provider()
        {
            Type = Type,
            Name = Name,
            BusinessName = BusinessName,
            BillingEmail = BillingEmail?.ToLowerInvariant().Trim()
        };
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        switch (Type)
        {
            case ProviderType.Msp:
                if (string.IsNullOrWhiteSpace(OwnerEmail))
                {
                    var ownerEmailDisplayName = nameof(OwnerEmail).GetDisplayAttribute<CreateProviderModel>()?.GetName() ?? nameof(OwnerEmail);
                    yield return new ValidationResult($"The {ownerEmailDisplayName} field is required.");
                }
                if (TeamsMinimumSeats < 0)
                {
                    var teamsMinimumSeatsDisplayName = nameof(TeamsMinimumSeats).GetDisplayAttribute<CreateProviderModel>()?.GetName() ?? nameof(TeamsMinimumSeats);
                    yield return new ValidationResult($"The {teamsMinimumSeatsDisplayName} field can not be negative.");
                }
                if (EnterpriseMinimumSeats < 0)
                {
                    var enterpriseMinimumSeatsDisplayName = nameof(EnterpriseMinimumSeats).GetDisplayAttribute<CreateProviderModel>()?.GetName() ?? nameof(EnterpriseMinimumSeats);
                    yield return new ValidationResult($"The {enterpriseMinimumSeatsDisplayName} field can not be negative.");
                }
                break;
            case ProviderType.Reseller:
                if (string.IsNullOrWhiteSpace(Name))
                {
                    var nameDisplayName = nameof(Name).GetDisplayAttribute<CreateProviderModel>()?.GetName() ?? nameof(Name);
                    yield return new ValidationResult($"The {nameDisplayName} field is required.");
                }
                if (string.IsNullOrWhiteSpace(BusinessName))
                {
                    var businessNameDisplayName = nameof(BusinessName).GetDisplayAttribute<CreateProviderModel>()?.GetName() ?? nameof(BusinessName);
                    yield return new ValidationResult($"The {businessNameDisplayName} field is required.");
                }
                if (string.IsNullOrWhiteSpace(BillingEmail))
                {
                    var billingEmailDisplayName = nameof(BillingEmail).GetDisplayAttribute<CreateProviderModel>()?.GetName() ?? nameof(BillingEmail);
                    yield return new ValidationResult($"The {billingEmailDisplayName} field is required.");
                }
                break;
        }
    }
}
