// Controllers/VideoController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WisdomVideoApp.Data;
using WisdomVideoApp.Models;
using WisdomVideoApp.Services;

namespace WisdomVideoApp.Controllers
{
    public class VideoController : Controller
    {
        private readonly VideoGenerationService _videoService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<VideoController> _logger;

        public VideoController(
            VideoGenerationService videoService,
            ApplicationDbContext context,
            ILogger<VideoController> logger)
        {
            _videoService = videoService;
            _context = context;
            _logger = logger;
        }

        // GET /Video/Create
        public IActionResult Create()
        {
            try
            {
                // Pass available styles to the view
                var styles = _videoService.GetAvailableStyles();
                ViewBag.Styles = styles;
                ViewBag.Categories = _videoService.GetStyleCategories();
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Create view");
                return View("Error", new { message = "Failed to load styles. Please check configuration." });
            }
        }

        // GET /Video/Styles - returns all available styles as JSON
        [HttpGet]
        public IActionResult GetStyles([FromQuery] string? category = null)
        {
            try
            {
                var styles = _videoService.GetAvailableStyles(category);
                return Ok(new
                {
                    categories = _videoService.GetStyleCategories(),
                    styles = styles,
                    total = styles.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting styles");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /Video/GetStyle/{id} - returns a specific style
        [HttpGet]
        public IActionResult GetStyle(string id)
        {
            try
            {
                var style = _videoService.GetStyle(id);
                if (style == null)
                    return NotFound(new { error = "Style not found" });
                return Ok(style);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting style {Id}", id);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /Video/Generate — streams progress via Server-Sent Events
        [HttpGet]
        public async Task Generate(  // Changed from IActionResult to Task
            [FromQuery] string script,
            [FromQuery] string voice = "en-US-AriaNeural",
            [FromQuery] string style = "doodle-handdrawn")
        {
            try
            {
                // Set SSE headers
                Response.Headers["Content-Type"] = "text/event-stream";
                Response.Headers["Cache-Control"] = "no-cache";
                Response.Headers["Connection"] = "keep-alive";

                async Task Send(string eventName, object data)
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(data,
                            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        await Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n");
                        await Response.Body.FlushAsync();
                    }
                    catch (Exception ex)
                    {
                        // Connection might be closed, log and continue
                        _logger.LogWarning("Failed to send SSE event: {Message}", ex.Message);
                    }
                }

                // ── Create job in database ────────────────────────
                var job = new VideoJob
                {
                    Script = script,
                    VoiceName = voice,
                    StyleId = style,
                    Status = "Processing"
                };

                _context.VideoJobs.Add(job);
                await _context.SaveChangesAsync();

                var progress = new Progress<VideoProgress>(async p =>
                {
                    try
                    {
                        await Send("progress", p);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Progress update failed: {Message}", ex.Message);
                    }
                });

                try
                {
                    var result = await _videoService.GenerateVideoAsync(
                        script,
                        voice,
                        style,
                        (IProgress<VideoProgress>)progress);

                    if (result.Success)
                    {
                        job.Status = "Completed";
                        job.VideoUrl = result.VideoUrl;
                        job.CompletedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();

                        await Send("complete", new
                        {
                            success = true,
                            jobId = job.JobId,
                            videoUrl = job.VideoUrl,
                            styleUsed = result.StyleUsed,
                            styleId = result.StyleId
                        });
                    }
                    else
                    {
                        job.Status = "Failed";
                        job.ErrorMessage = result.Error;
                        job.CompletedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();

                        await Send("error", new { message = result.Error });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating video for job {JobId}", job.JobId);
                    job.Status = "Failed";
                    job.ErrorMessage = ex.Message;
                    job.CompletedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    await Send("error", new { message = ex.Message });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in Generate action");
                // Can't send error response here as headers might already be sent
            }
            finally
            {
                // Ensure the response is properly closed
                try
                {
                    await Response.Body.FlushAsync();
                    Response.Body.Close();
                }
                catch { /* Ignore disposal errors */ }
            }
        }

        // POST /Video/GenerateJson - JSON API endpoint for programmatic access
        [HttpPost]
        public async Task<IActionResult> GenerateJson([FromBody] VideoRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Script))
                return BadRequest(new { error = "Script is required" });

            try
            {
                var progress = new Progress<VideoProgress>(p =>
                {
                    // Optionally log progress
                });

                var result = await _videoService.GenerateVideoAsync(
                    request.Script,
                    request.VoiceName,
                    request.StyleId,
                    progress);

                if (!result.Success)
                    return BadRequest(result);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GenerateJson");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /Video/Download/{jobId}
        public async Task<IActionResult> Download(string jobId)
        {
            try
            {
                var job = await _context.VideoJobs
                    .FirstOrDefaultAsync(j => j.JobId == jobId);

                if (job == null || job.Status != "Completed" || string.IsNullOrEmpty(job.VideoUrl))
                    return NotFound();

                var relativePath = job.VideoUrl
                    .TrimStart('/')
                    .Replace('/', Path.DirectorySeparatorChar);

                var path = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    relativePath);

                if (!System.IO.File.Exists(path))
                    return NotFound();

                return PhysicalFile(path, "video/mp4", "wisdom_video.mp4");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading video {JobId}", jobId);
                return StatusCode(500, new { error = "Failed to download video" });
            }
        }

        // GET /Video/Status/{jobId} - Check job status
        [HttpGet]
        public async Task<IActionResult> Status(string jobId)
        {
            try
            {
                var job = await _context.VideoJobs
                    .FirstOrDefaultAsync(j => j.JobId == jobId);

                if (job == null)
                    return NotFound(new { error = "Job not found" });

                return Ok(new
                {
                    jobId = job.JobId,
                    status = job.Status,
                    videoUrl = job.VideoUrl,
                    errorMessage = job.ErrorMessage,
                    createdAt = job.CreatedAt,
                    completedAt = job.CompletedAt,
                    styleId = job.StyleId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting status for {JobId}", jobId);
                return StatusCode(500, new { error = "Failed to get job status" });
            }
        }

        // GET /Video/History - Get recent jobs
        [HttpGet]
        public async Task<IActionResult> History([FromQuery] int limit = 10)
        {
            try
            {
                var jobs = await _context.VideoJobs
                    .OrderByDescending(j => j.CreatedAt)
                    .Take(limit)
                    .Select(j => new
                    {
                        j.JobId,
                        j.Status,
                        j.VideoUrl,
                        j.ErrorMessage,
                        j.CreatedAt,
                        j.CompletedAt,
                        j.VoiceName,
                        j.StyleId
                    })
                    .ToListAsync();

                return Ok(jobs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting history");
                return StatusCode(500, new { error = "Failed to get history" });
            }
        }

        // GET /Video/DownloadV2/{jobId} - Alternative download with filename
        [HttpGet]
        public async Task<IActionResult> DownloadV2(string jobId, [FromQuery] string filename = "wisdom_video.mp4")
        {
            try
            {
                var job = await _context.VideoJobs
                    .FirstOrDefaultAsync(j => j.JobId == jobId);

                if (job == null || job.Status != "Completed" || string.IsNullOrEmpty(job.VideoUrl))
                    return NotFound();

                var relativePath = job.VideoUrl
                    .TrimStart('/')
                    .Replace('/', Path.DirectorySeparatorChar);

                var path = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    relativePath);

                if (!System.IO.File.Exists(path))
                    return NotFound();

                return PhysicalFile(path, "video/mp4", filename);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading video {JobId} with filename {Filename}", jobId, filename);
                return StatusCode(500, new { error = "Failed to download video" });
            }
        }
    }
}