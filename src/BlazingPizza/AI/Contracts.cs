using BlazingPizza.Shared;

namespace BlazingPizza.Server.AI;

public sealed record SearchResult(int Id, string Name, string Kind, string? Description = null, decimal? Price = null, Dictionary<int, decimal>? SizePrices = null);
public sealed record CartPlan(CartAction[] Actions);
public sealed record CartAction(string Type, int? SpecialId = null, int Quantity = 1, int Size = Pizza.DefaultSize, int[]? ToppingIds = null);
public sealed record CartRequest(string Utterance, string UserId = "demo");


