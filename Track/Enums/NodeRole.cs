namespace Track.Enums;

/// <summary>Where an agent's Docker engine sits: outside any swarm, or a swarm worker or manager.</summary>
public enum NodeRole
{
    Standalone = 0,
    Worker = 1,
    Manager = 2,
}
