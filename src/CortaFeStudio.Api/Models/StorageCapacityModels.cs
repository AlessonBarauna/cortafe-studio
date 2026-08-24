namespace CortaFeStudio.Api.Models;

public enum StorageOperation { Acquisition, Transcription, BatchRender }

public sealed class StorageCapacityReport
{
    public StorageOperation Operation { get; set; }
    public long AvailableBytes { get; set; }
    public long EstimatedBytes { get; set; }
    public long SafetyReserveBytes { get; set; }
    public bool Allowed { get; set; }
    public string Message { get; set; } = "";
}
