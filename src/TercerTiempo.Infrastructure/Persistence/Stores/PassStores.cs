using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Passes;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class MatchPassStore(AppDbContext db) : IMatchPassStore
{
    public Task<MatchPass?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.MatchPasses.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<MatchPass?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = InviteCodes.Normalize(code);
        return db.MatchPasses.FirstOrDefaultAsync(p => p.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<MatchPass>> GetBySquadMatchAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        await db.MatchPasses.Where(p => p.SquadId == squadId && p.MatchId == matchId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchPass>> GetRedeemedByAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.MatchPasses.Where(p => p.RedeemedByUserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchPass>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.MatchPasses.Where(p => list.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public void Add(MatchPass pass) => db.MatchPasses.Add(pass);
}

internal sealed class MatchPassRequestStore(AppDbContext db) : IMatchPassRequestStore
{
    public Task<MatchPassRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.MatchPassRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MatchPassRequest>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        await db.MatchPassRequests.Where(r => r.ClubId == clubId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchPassRequest>> GetByRequesterAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.MatchPassRequests.Where(r => r.RequesterUserId == userId).ToListAsync(cancellationToken);

    public void Add(MatchPassRequest request) => db.MatchPassRequests.Add(request);
}

internal sealed class MatchPassPriceStore(AppDbContext db) : IMatchPassPriceStore
{
    public Task<MatchPassPrice?> GetAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        db.MatchPassPrices.FirstOrDefaultAsync(p => p.SquadId == squadId && p.MatchId == matchId, cancellationToken);

    public async Task<IReadOnlyList<MatchPassPrice>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        await db.MatchPassPrices.Where(p => p.ClubId == clubId).ToListAsync(cancellationToken);

    public void Add(MatchPassPrice price) => db.MatchPassPrices.Add(price);

    public void Remove(MatchPassPrice price) => db.MatchPassPrices.Remove(price);
}

internal sealed class PassSaleStore(AppDbContext db) : IPassSaleStore
{
    public async Task<IReadOnlyList<PassSale>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        await db.PassSales.Where(s => s.ClubId == clubId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PassSale>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.PassSales.ToListAsync(cancellationToken);

    public void Add(PassSale sale) => db.PassSales.Add(sale);
}
