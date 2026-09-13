using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Domain.Security;

public static class CommercialAuthorizationPolicy
{
    public const decimal AbsoluteMaximumDiscountPercentage = 80m;

    public static decimal CalculateFinalPrice(
        decimal unitCost,
        decimal basePrice,
        decimal discountPercentage,
        decimal roleMaximumPercentage,
        bool wholesalePriceApplied)
    {
        if (unitCost < 0 || basePrice < 0) throw new DomainRuleViolationException("Cost and price cannot be negative.");
        if (discountPercentage < 0) throw new DomainRuleViolationException("Discount cannot be negative.");
        if (roleMaximumPercentage is < 0 or > AbsoluteMaximumDiscountPercentage)
            throw new DomainRuleViolationException("Role discount limit cannot exceed 80 percent.");
        if (discountPercentage > roleMaximumPercentage || discountPercentage > AbsoluteMaximumDiscountPercentage)
            throw new DomainRuleViolationException("Discount exceeds the applicable limit.");
        if (wholesalePriceApplied && discountPercentage > 0)
            throw new DomainRuleViolationException("Wholesale price and manual discount cannot be combined.");

        var finalPrice = decimal.Round(basePrice * (1m - discountPercentage / 100m), 4, MidpointRounding.AwayFromZero);
        if (finalPrice < unitCost) throw new DomainRuleViolationException("A sale below cost is not allowed.");
        return finalPrice;
    }

    public static void EnsureIndependentAuthorization(
        Guid executorUserId,
        Guid authorizerUserId,
        RoleLevel authorizerRole)
    {
        if (executorUserId == Guid.Empty || authorizerUserId == Guid.Empty)
            throw new DomainRuleViolationException("Executor and authorizer are required.");
        if (executorUserId == authorizerUserId)
            throw new DomainRuleViolationException("Executor and authorizer must be different users.");
        if (authorizerRole is not (RoleLevel.Supervisor or RoleLevel.Administrador))
            throw new DomainRuleViolationException("Authorizer must be Supervisor or Administrator.");
    }
}
