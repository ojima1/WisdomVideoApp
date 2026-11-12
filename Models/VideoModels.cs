// Models/VideoModels.cs
namespace WisdomVideoApp.Models
{
    // DTO for incoming requests
    public class VideoRequest
    {
        public string Script { get; set; } = string.Empty;
        public string VoiceName { get; set; } = "en-US-AriaNeural";
        public string StyleId { get; set; } = "doodle-handdrawn";
    }

    // DTO for responses
    public class VideoResult
    {
        public bool Success { get; set; }
        public string? VideoUrl { get; set; }
        public string? JobId { get; set; }
        public string? Error { get; set; }
        public string? StyleUsed { get; set; }
        public string? StyleId { get; set; }
    }

    // DTO for progress tracking
    public class VideoProgress
    {
        public string Step { get; set; } = string.Empty;
        public int Percent { get; set; }
        public string? CurrentStatement { get; set; }
    }

    // Entity for database persistence
    public class VideoJob
    {
        public int Id { get; set; }
        public string JobId { get; set; } = Guid.NewGuid().ToString();
        public string Script { get; set; } = string.Empty;
        public string VoiceName { get; set; } = "en-US-AriaNeural";
        public string StyleId { get; set; } = "doodle-handdrawn";
        public string Status { get; set; } = "Pending"; // Pending, Processing, Completed, Failed
        public string? VideoUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }

    // Style model
    public class VideoStyle
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string StylePrefix { get; set; } = string.Empty;
        public string StyleSuffix { get; set; } = string.Empty;
        public string PreviewImage { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public Dictionary<string, string>? Metadata { get; set; }
    }
}