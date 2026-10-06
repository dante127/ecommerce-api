using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using ECommerce.Application.Features.Auth.DTOs;
using MediatR;

namespace ECommerce.Application.Features.Auth.Queries.GetMe;

public sealed record GetMeQuery : IRequest<Result<UserResponse>>;

public sealed class GetMeQueryHandler : IRequestHandler<GetMeQuery, Result<UserResponse>>
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IIdentityService _identityService;

    public GetMeQueryHandler(ICurrentUserService currentUserService, IIdentityService identityService)
    {
        _currentUserService = currentUserService;
        _identityService = identityService;
    }

    public async Task<Result<UserResponse>> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || !_currentUserService.UserId.HasValue)
        {
            return Result<UserResponse>.Failure(Error.Unauthenticated);
        }

        return await _identityService.GetUserByIdAsync(_currentUserService.UserId.Value, cancellationToken);
    }
}
