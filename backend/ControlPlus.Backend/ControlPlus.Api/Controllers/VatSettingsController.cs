using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/settings/vat")]
public sealed class VatSettingsController(CurrentActorContextResolver actors, ISalesService sales)
    : ApiControllerBase(actors)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.TaxesManage)]
    [ProducesResponseType<VatSettingDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await sales.GetVatSettingAsync(actor, cancellationToken), Ok);
    }

    [HttpPut]
    [PermissionAuthorize(PermissionCodes.TaxesManage)]
    [ProducesResponseType<VatSettingDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Put([FromBody] UpdateVatSettingRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await sales.UpdateVatSettingAsync(actor, request, cancellationToken), Ok);
    }
}
