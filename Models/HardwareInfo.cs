namespace TomatoFlow.Models;

public sealed record HardwareInfo(
    string CpuName,
    int LogicalCores,
    double RamGb,
    string GpuName,
    double VramGb,
    bool HasNvidia)
{
    public string BestModelId
    {
        get
        {
            if (HasNvidia && VramGb >= 4.5)
            {
                return "turbo";
            }

            if (RamGb >= 15 && LogicalCores >= 8)
            {
                return "turbo";
            }

            if (RamGb >= 11)
            {
                return "medium.en";
            }

            if (RamGb >= 7)
            {
                return "small.en";
            }

            return "base.en";
        }
    }

    public string Summary
    {
        get
        {
            var gpu = HasNvidia ? $"{GpuName}  -  {VramGb:0.#} GB VRAM" : GpuName;
            return $"{CpuName}  -  {LogicalCores} threads  -  {RamGb:0.#} GB RAM  -  {gpu}";
        }
    }

    public string RecommendationReason
    {
        get
        {
            if (HasNvidia && VramGb >= 4.5)
            {
                return $"{GpuName} detected. Large v3 Turbo will run on CUDA for near-instant dictation.";
            }

            if (RamGb >= 15 && LogicalCores >= 8)
            {
                return "Strong CPU and plenty of RAM. Large v3 Turbo is the best fit.";
            }

            if (RamGb >= 11)
            {
                return "Solid machine. Medium English gives the best balance of speed and accuracy.";
            }

            if (RamGb >= 7)
            {
                return "Modest hardware. Small English keeps things fast without losing too much accuracy.";
            }

            return "Low-power machine. Base English is the smart pick for a smooth experience.";
        }
    }
}
