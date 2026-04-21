namespace BezelFreeCorrection.Topology;

public enum TopologyKind
{
    // One logical display spanning all physical monitors (e.g., NVIDIA Surround).
    Surround,

    // Multiple independent displays — Windows sees each monitor separately.
    Separate,
}
