using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/customers")]
public sealed class CustomersController(CurrentActorContextResolver actorContextResolver, ICustomerService customers) : ApiControllerBase(actorContextResolver)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.CustomersBasicManage + "|" + PermissionCodes.CustomersManage)]
    [ProducesResponseType<PagedResult<CustomerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] CustomerListQuery query, CancellationToken cancellationToken)
    { var actor = await GetActorAsync(cancellationToken); return actor is null ? MissingActor() : FromResult(await customers.ListAsync(actor, query, cancellationToken), Ok); }

    [HttpGet("{customerId:guid}")]
    [PermissionAuthorize(PermissionCodes.CustomersBasicManage + "|" + PermissionCodes.CustomersManage)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid customerId, CancellationToken cancellationToken)
    { var actor = await GetActorAsync(cancellationToken); return actor is null ? MissingActor() : FromResult(await customers.GetAsync(actor, customerId, cancellationToken), Ok); }

    [HttpPost]
    [PermissionAuthorize(PermissionCodes.CustomersBasicManage + "|" + PermissionCodes.CustomersManage)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    { var actor = await GetActorAsync(cancellationToken); if (actor is null) return MissingActor(); var result = await customers.CreateAsync(actor, request, cancellationToken); return FromResult(result, x => CreatedAtAction(nameof(Get), new { customerId = x.Id }, x)); }

    [HttpPut("{customerId:guid}")]
    [PermissionAuthorize(PermissionCodes.CustomersBasicManage + "|" + PermissionCodes.CustomersManage)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken)
    { var actor = await GetActorAsync(cancellationToken); return actor is null ? MissingActor() : FromResult(await customers.UpdateAsync(actor, customerId, request, cancellationToken), Ok); }

    [HttpPut("{customerId:guid}/status")]
    [PermissionAuthorize(PermissionCodes.CustomersManage)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetStatus(Guid customerId, SetCustomerStatusRequest request, CancellationToken cancellationToken)
    { var actor = await GetActorAsync(cancellationToken); return actor is null ? MissingActor() : FromResult(await customers.SetStatusAsync(actor, customerId, request, cancellationToken), Ok); }
}
