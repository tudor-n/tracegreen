using TraceGreen.Domain.Ports;
using TraceGreen.Infrastructure.Memory;
using TraceGreen.Tests.Contracts;

namespace TraceGreen.Tests.Infrastructure;

public class MemoryRepositoryTests : RepositoryContract
{
    protected override async Task<IRepository> MakeAsync()
    {
        var repo = new MemoryRepository();
        await repo.SeedAsync(Bundle, reset: true, CancellationToken.None);
        return repo;
    }
}
