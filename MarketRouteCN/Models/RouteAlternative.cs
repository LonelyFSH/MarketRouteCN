namespace MarketRouteCN.Models;

public sealed record RouteAlternative(
    int ServerCount,
    long TotalCost,
    int OverbuyQuantity,
    IReadOnlyList<string> Worlds);
