using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ECCR.Models;

namespace ECCR.Services.GameDiscovery;

/// <summary>
/// One game-launcher scanner. Implementations must tolerate a completely absent launcher
/// (return an empty list, never throw) and any malformed/partial manifest file (skip it, keep
/// scanning the rest) - <see cref="GameLibraryService.RescanAsync"/> also wraps each provider's
/// call in its own try/catch, but providers should not rely on that as their only defense.
/// </summary>
public interface IGameDiscoveryProvider
{
    GameSource Source { get; }

    Task<List<GameInfo>> ScanAsync(CancellationToken ct);
}
