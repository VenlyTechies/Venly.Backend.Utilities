namespace Venly.Corridor.Helper;

public interface ICorridorClient
{
    /// <summary>
    /// The enabled routes, from cache where it is fresh.
    ///
    /// <para>
    /// NULL means "we do not know" — never fetched successfully, so there is no list to check against and no
    /// stale one to fall back on. That is different from an empty list, which means PaymentService answered
    /// and has no open routes. Callers must distinguish the two: treating null as empty would refuse every
    /// corridor on a cold start, and treating it as "allow anything" would reopen the unvalidated free string
    /// this client exists to close.
    /// </para>
    /// </summary>
    Task<CorridorSnapshot?> GetSnapshotAsync(CancellationToken ct = default);
}
