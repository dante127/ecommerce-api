using ECommerce.Application.Common.Interfaces;
using ECommerce.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Application.Features.Auth.Commands.RevokeToken;

public sealed record RevokeTokenCommand(string RefreshToken) : IRequest<Result>;

public sealed class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly TimeProvider _timeProvider;

    public RevokeTokenCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        TimeProvider timeProvider)
    {
        _context = context;
        _tokenService = tokenService;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(RevokeTokenCommand request, CancellationToken cancellationToken)
    {
        var incomingHash = _tokenService.HashToken(request.RefreshToken);
        var now = _timeProvider.GetUtcNow();

        await _context.RefreshTokens
            .Where(t => t.TokenHash == incomingHash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);

        return Result.Success();
    }
}
