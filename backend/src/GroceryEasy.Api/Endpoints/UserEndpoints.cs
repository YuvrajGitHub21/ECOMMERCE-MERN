using GroceryEasy.Api.Extensions;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Application.Features.Users.GetCurrentUser;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Api.Endpoints;

/// <summary>User-facing profile endpoints.</summary>
internal sealed class UserEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app
            .MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/me", GetMeAsync)
            .WithSummary("Returns the signed-in user's profile.");
    }

    private static async Task<IResult> GetMeAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<CurrentUserResponse> result =
            await dispatcher.Query(new GetCurrentUserQuery(), cancellationToken);

        return result.ToHttpResult();
    }
}
