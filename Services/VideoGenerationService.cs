using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using WisdomVideoApp.Models;

namespace WisdomVideoApp.Services
{
    public class VideoGenerationService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<VideoGenerationService> _logger;
        private readonly string _workDir;

        private static readonly SemaphoreSlim _imageGenLock = new(1, 1);
        private static DateTime _lastImageGenCall = DateTime.MinValue;

        private const int ImageGenMinIntervalMs = 5000;
        private const int MaxRetries = 3;
        private const int OutputFps = 25;
        private const int ImageWidth = 1280;
        private const int ImageHeight = 720;
        private const string TtsSpeechRate = "+0%";

        private const string SilenceTrimFilter =
            "silenceremove=start_periods=1:start_duration=0:start_threshold=-45dB:detection=peak:" +
            "stop_periods=-1:stop_duration=0.15:stop_threshold=-45dB:detection=peak," +
            "areverse," +
            "silenceremove=start_periods=1:start_duration=0:start_threshold=-45dB:detection=peak:" +
            "stop_periods=-1:stop_duration=0.15:stop_threshold=-45dB:detection=peak," +
            "areverse";

        // Edge TTS voices
        private readonly Dictionary<string, string> _voiceOptions = new()
        {
            ["en-US-AriaNeural"] = "Aria — Warm Female (US)",
            ["en-US-JennyNeural"] = "Jenny — Friendly Female (US)",
            ["en-US-GuyNeural"] = "Guy — Deep Male (US)",
            ["en-US-DavisNeural"] = "Davis — Calm Male (US)",
            ["en-US-JaneNeural"] = "Jane — Bright Female (US)",
            ["en-US-JasonNeural"] = "Jason — Authoritative Male (US)",
            ["en-US-NancyNeural"] = "Nancy — Expressive Female (US)",
            ["en-US-SaraNeural"] = "Sara — Soothing Female (US)",
            ["en-US-TonyNeural"] = "Tony — Cheerful Male (US)",
            ["en-GB-SoniaNeural"] = "Sonia — British Female (UK)",
            ["en-GB-RyanNeural"] = "Ryan — British Male (UK)",
            ["en-AU-NatashaNeural"] = "Natasha — Australian Female (AU)",
            ["en-AU-WilliamNeural"] = "William — Australian Male (AU)",
            ["en-IN-NeerjaNeural"] = "Neerja — Indian Female (IN)",
            ["en-IN-PrabhatNeural"] = "Prabhat — Indian Male (IN)",
            ["en-NG-EzinneNeural"] = "Ezinne — Nigerian Female (NG)",
            ["en-NG-AbeoNeural"] = "Abeo — Nigerian Male (NG)",
            ["en-ZA-LeahNeural"] = "Leah — South African Female (ZA)",
            ["en-ZA-LukeNeural"] = "Luke — South African Male (ZA)",
            ["en-KE-AsiliaNeural"] = "Asilia — Kenyan Female (KE)",
            ["en-KE-ChilembaNeural"] = "Chilemba — Kenyan Male (KE)"
        };

        public Dictionary<string, string> GetVoiceOptions() => _voiceOptions;

        // ============================================================
        // STYLES CATALOG - ALL WITH BRIGHT LIGHTING ?
        // ============================================================
        private readonly Dictionary<string, VideoStyle> _availableStyles = new()
        {
            // === CINEMATIC STYLES ===
            ["cinematic-drama"] = new VideoStyle
            {
                Id = "cinematic-drama",
                Name = "Cinematic Drama",
                Description = "Bright cinematic style with warm golden lighting and clear visibility",
                Category = "Cinematic",
                StylePrefix = "Cinematic movie still, bright well-lit scene with warm golden hour lighting, clear visibility of all subjects, soft natural shadows, film grain texture, vibrant cinematic color grading (warm golden and teal tones), single faceless figure in clear view, bright atmospheric lighting, ",
                StyleSuffix = ", cinematic 16:9 widescreen composition, rule of thirds framing, single clear focal point, generous negative space in upper third for text overlay, no text anywhere in image, Hollywood film style, bright and inviting atmosphere"
            },
            ["cinematic-vintage"] = new VideoStyle
            {
                Id = "cinematic-vintage",
                Name = "Vintage Cinema",
                Description = "Classic 1970s film look with warm bright tones",
                Category = "Cinematic",
                StylePrefix = "Bright vintage 1970s film still, warm golden tones, clear visibility, soft focus, film grain texture, subtle lens flare, single faceless character in period-appropriate pose, bright golden hour lighting, ",
                StyleSuffix = ", 16:9 cinematic composition, single focal point, empty space in upper third, no text anywhere in image, classic cinema aesthetic, bright nostalgic film look"
            },
            ["cinematic-noir"] = new VideoStyle
            {
                Id = "cinematic-noir",
                Name = "Film Noir",
                Description = "Stylish black and white with dramatic but visible lighting",
                Category = "Cinematic",
                StylePrefix = "Film noir style, high contrast black and white, bright highlights and clear visibility, single faceless figure in mysterious pose, stylish shadows, smoke or fog atmosphere for mood, ",
                StyleSuffix = ", 16:9 cinematic composition, single focal point, generous negative space, no text anywhere in image, classic noir atmosphere, stylish and visible"
            },

            // === ANIMATION STYLES ===
            ["animation-disney"] = new VideoStyle
            {
                Id = "animation-disney",
                Name = "Disney Animation",
                Description = "Classic Disney-style animation with bright vibrant colors",
                Category = "Animation",
                StylePrefix = "Disney-style 2D animation, bright vibrant colors, smooth clean lines, soft shading, single faceless character with cartoon proportions, warm inviting atmosphere, magical bright lighting, ",
                StyleSuffix = ", 16:9 widescreen composition, single clear focal point, generous negative space in upper third, no text anywhere in image, Disney animation style, bright and magical"
            },
            ["animation-anime"] = new VideoStyle
            {
                Id = "animation-anime",
                Name = "Anime Style",
                Description = "Japanese anime aesthetic with bright colors and clear lines",
                Category = "Animation",
                StylePrefix = "Japanese anime style, bold clean lines, bright vibrant colors, clear visibility, single faceless character with anime proportions, dynamic and expressive pose, bright pastel or saturated colors, ",
                StyleSuffix = ", 16:9 anime composition, single clear focal point, generous negative space in upper third, no text anywhere in image, anime style, bright and dynamic"
            },
            ["animation-stop-motion"] = new VideoStyle
            {
                Id = "animation-stop-motion",
                Name = "Stop Motion",
                Description = "Bright claymation and stop-motion animation look with texture",
                Category = "Animation",
                StylePrefix = "Stop-motion clay animation style, textured surfaces, clear visibility, bright soft lighting, single faceless character made of clay or fabric, warm bright colors, charming handmade look, ",
                StyleSuffix = ", 16:9 composition, single focal point, empty space in upper third, no text anywhere in image, stop-motion animation style, bright and charming"
            },
            ["animation-pixel"] = new VideoStyle
            {
                Id = "animation-pixel",
                Name = "Pixel Art Animation",
                Description = "Retro 8-bit pixel art with bright colors and clear shapes",
                Category = "Animation",
                StylePrefix = "Pixel art, retro 8-bit video game style, crisp blocky pixels, bright colors, clear visibility, single faceless character sprite, flat bright colors, simple geometric shapes, ",
                StyleSuffix = ", 16:9 game screen composition, single focal point, generous space in upper third, no text anywhere in image, retro gaming pixel art style, bright and nostalgic"
            },

            // === DOODLE / SKETCH STYLES ===
            ["doodle-handdrawn"] = new VideoStyle
            {
                Id = "doodle-handdrawn",
                Name = "Hand-Drawn Doodle",
                Description = "Casual sketchbook doodle style with bright playful lines",
                Category = "Doodle/Sketch",
                StylePrefix = "Casual hand-drawn doodle, playful sketchy lines, bright white notebook paper background, clear visibility, single faceless stick-figure character, loose spontaneous drawing style, bright colorful gel pen aesthetic, ",
                StyleSuffix = ", 16:9 composition, single clear focal point, generous negative space in upper third, no text anywhere in image, doodle sketch style, bright and fun"
            },
            ["doodle-charcoal"] = new VideoStyle
            {
                Id = "doodle-charcoal",
                Name = "Charcoal Sketch",
                Description = "Expressive charcoal drawing with visible dramatic shadows",
                Category = "Doodle/Sketch",
                StylePrefix = "Expressive charcoal sketch, deep rich black tones with bright white highlights, clear visibility, textured paper surface, single faceless figure with dramatic pose, strong contrast, visible eraser marks, ",
                StyleSuffix = ", 16:9 composition, single focal point, generous negative space in upper third, no text anywhere in image, charcoal sketch style, dramatic and visible"
            },
            ["doodle-cartoon"] = new VideoStyle
            {
                Id = "doodle-cartoon",
                Name = "Cartoon Doodle",
                Description = "Fun cartoon style with bright bold outlines and colors",
                Category = "Doodle/Sketch",
                StylePrefix = "Fun cartoon doodle, thick bold bright outlines, bright playful colors, simple shapes, clear visibility, single faceless character in cartoon style, bright white background, expressive and dynamic pose, ",
                StyleSuffix = ", 16:9 composition, single focal point, generous space in upper third, no text anywhere in image, cartoon doodle style, bright and energetic"
            },

            // === NIGERIAN FOLKTALE STYLES ===
            ["folktale-traditional"] = new VideoStyle
            {
                Id = "folktale-traditional",
                Name = "Traditional Nigerian Folktale",
                Description = "Vibrant traditional Nigerian art with bright colors and clear visibility",
                Category = "Nigerian/African",
                StylePrefix = "Traditional Nigerian folktale illustration, bright vibrant colors, bold geometric patterns, warm earth tones with bright accent colors, clear visibility of all elements, single faceless figure in traditional West African pose, decorative border patterns, bright storytelling composition, ",
                StyleSuffix = ", 16:9 composition, single clear focal point, generous negative space in upper third, no text anywhere in image, Nigerian folktale style, bright rich cultural heritage"
            },
            ["folktale-ancestral"] = new VideoStyle
            {
                Id = "folktale-ancestral",
                Name = "Ancestral Spirit",
                Description = "Mystical Nigerian art with bright spiritual lighting",
                Category = "Nigerian/African",
                StylePrefix = "Mystical Nigerian ancestral art, bright spiritual atmosphere, warm earth tones with bright gold accents, clear visibility, single faceless figure in meditative pose, ancestral symbols, bright glowing radiant elements, ",
                StyleSuffix = ", 16:9 composition, single focal point, generous space in upper third, no text anywhere in image, ancestral Nigerian art style, bright spiritual and reverent"
            },
            ["folktale-ankara"] = new VideoStyle
            {
                Id = "folktale-ankara",
                Name = "Ankara Print Style",
                Description = "Vibrant Ankara fabric-inspired art with bright bold prints",
                Category = "Nigerian/African",
                StylePrefix = "Ankara print inspired illustration, bright vibrant African fabric patterns, rich bright colors, clear visibility, geometric and organic pattern overlays, single faceless figure in contemporary Nigerian style, bright joyful composition, ",
                StyleSuffix = ", 16:9 composition, single focal point, generous space in upper third, no text anywhere in image, Ankara print style, bright vibrant Nigerian aesthetic"
            },
            ["folktale-calabash"] = new VideoStyle
            {
                Id = "folktale-calabash",
                Name = "Calabash Storytelling",
                Description = "Traditional calabash carving style with warm bright tones",
                Category = "Nigerian/African",
                StylePrefix = "Traditional calabash carving inspired illustration, bright warm golden-brown tones, engraved line work style, clear visibility, single faceless figure in traditional storytelling pose, circular composition, decorative indigenous patterns, ",
                StyleSuffix = ", 16:9 composition, single focal point, generous negative space in upper third, no text anywhere in image, calabash carving style, bright traditional storytelling"
            },

            // === PAINTING STYLES ===
            ["painting-watercolor"] = new VideoStyle
            {
                Id = "painting-watercolor",
                Name = "Watercolor Painting",
                Description = "Soft bright watercolor with gentle pastel tones",
                Category = "Painting",
                StylePrefix = "Watercolor painting, soft bright colors, flowing gentle washes, paper texture visible, single faceless figure with simple features, bright warm paper background, soft pastel palette, clear visibility, ",
                StyleSuffix = ", 16:9 composition, single clear focal point, generous empty space in upper third, no text anywhere in image, watercolor illustration style, bright and dreamy"
            },
            ["painting-oil"] = new VideoStyle
            {
                Id = "painting-oil",
                Name = "Oil Painting",
                Description = "Rich oil painting with bright colors and visible brush strokes",
                Category = "Painting",
                StylePrefix = "Oil painting on canvas, bright rich colors, visible brush strokes, warm bright lighting, single faceless figure, deep rich colors, canvas texture visible, classical painting style, clear visibility, ",
                StyleSuffix = ", 16:9 composition, single focal point, bright lighting, no text anywhere in image, oil painting style, bright and classical"
            },

            // === MODERN STYLES ===
            ["modern-minimal"] = new VideoStyle
            {
                Id = "modern-minimal",
                Name = "Modern Minimalist",
                Description = "Clean bright vector art with bold colors and simple shapes",
                Category = "Modern",
                StylePrefix = "Modern minimalist illustration, bright flat vector art, clean geometric shapes, bold bright solid colors, clear visibility, simple faceless character with clean lines, bright single color background, ",
                StyleSuffix = ", 16:9 composition, single focal point, generous negative space in upper third, no text anywhere in image, contemporary illustration style, bright and modern"
            },

            // === FANTASY STYLES ===
            ["fantasy-surreal"] = new VideoStyle
            {
                Id = "fantasy-surreal",
                Name = "Surreal Fantasy",
                Description = "Dreamy surreal art with bright magical elements",
                Category = "Fantasy",
                StylePrefix = "Surreal fantasy illustration, bright dreamlike atmosphere, floating elements, soft glowing light, single faceless figure in mystical pose, bright ethereal colors, magical effects and sparkles, clear visibility, ",
                StyleSuffix = ", 16:9 composition, single focal point, generous negative space, no text anywhere in image, surreal fantasy style, bright and magical"
            },

            // === ARTISTIC STYLES ===
            ["art-mosaic"] = new VideoStyle
            {
                Id = "art-mosaic",
                Name = "Mosaic Tiles",
                Description = "Colorful mosaic tile art with bright jewel tones",
                Category = "Artistic",
                StylePrefix = "Mosaic tile art, bright colorful glass tiles, Byzantine style, gold leaf accents, single faceless figure in mosaic form, bright jewel tones, intricate tile patterns, clear visibility, ",
                StyleSuffix = ", 16:9 composition, single focal point, generous negative space, no text anywhere in image, mosaic tile style, bright and decorative"
            },
            ["art-chalkboard"] = new VideoStyle
            {
                Id = "art-chalkboard",
                Name = "Chalkboard Art",
                Description = "Bright chalk drawing on dark board with clear visible lines",
                Category = "Artistic",
                StylePrefix = "Chalk drawing on dark chalkboard, bright white and pastel chalk lines, clear visibility, single faceless figure drawn in chalk, dark textured background, sketchy and bright, ",
                StyleSuffix = ", 16:9 composition, single focal point, generous negative space, no text anywhere in image, chalkboard art style, bright and educational"
            },
            ["art-stained-glass"] = new VideoStyle
            {
                Id = "art-stained-glass",
                Name = "Stained Glass",
                Description = "Beautiful stained glass with bright colorful panels",
                Category = "Artistic",
                StylePrefix = "Stained glass window art, bright colorful glass panels, lead lines between sections, single faceless figure in stained glass form, vibrant bright jewel tones, light filtering through effect, clear visibility, ",
                StyleSuffix = ", 16:9 composition, single focal point, generous space in upper third, no text anywhere in image, stained glass style, bright and luminous"
            }
        };

        public VideoGenerationService(
            HttpClient http,
            IConfiguration config,
            ILogger<VideoGenerationService> logger,
            IWebHostEnvironment env)
        {
            _http = http;
            _config = config;
            _logger = logger;

            string webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
            _workDir = Path.Combine(webRoot, "output");
            Directory.CreateDirectory(_workDir);
        }

        // ============================================================
        // Public methods
        // ============================================================

        public List<VideoStyle> GetAvailableStyles(string? category = null)
        {
            var styles = _availableStyles.Values.Where(s => s.IsActive);
            if (!string.IsNullOrEmpty(category))
                styles = styles.Where(s => s.Category == category);
            return styles.ToList();
        }

        public List<string> GetStyleCategories()
        {
            return _availableStyles.Values
                .Where(s => s.IsActive)
                .Select(s => s.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList()!;
        }

        public VideoStyle? GetStyle(string styleId)
        {
            _availableStyles.TryGetValue(styleId, out var style);
            return style;
        }

        // ============================================================
        // Main Generate Method
        // ============================================================

        public async Task<VideoResult> GenerateVideoAsync(
            string script,
            string voice,
            string styleId,
            IProgress<VideoProgress> progress)
        {
            if (!_availableStyles.TryGetValue(styleId, out var selectedStyle))
            {
                _logger.LogWarning("Style '{StyleId}' not found, falling back to hand-drawn doodle style", styleId);
                selectedStyle = _availableStyles["doodle-handdrawn"];
            }

            var jobId = Guid.NewGuid().ToString("N")[..8];
            var jobDir = Path.Combine(_workDir, jobId);
            Directory.CreateDirectory(jobDir);

            try
            {
                progress.Report(new VideoProgress { Step = "Splitting script...", Percent = 5 });
                var statements = SplitScript(script);

                if (statements.Count == 0)
                    return new VideoResult { Success = false, Error = "Script is empty or has no valid statements." };

                var batchCount = (int)Math.Ceiling(statements.Count / 30.0);
                progress.Report(new VideoProgress
                {
                    Step = $"Generating image prompts via Groq ({statements.Count} lines, {batchCount} batch{(batchCount > 1 ? "es" : "")})...",
                    Percent = 10
                });
                var prompts = await GenerateImagePromptsAsync(statements, selectedStyle);

                var clipPaths = new List<string>();
                double totalDurationAccumulator = 0;

                for (int i = 0; i < statements.Count; i++)
                {
                    var pct = 15 + (i * 75 / statements.Count);
                    progress.Report(new VideoProgress
                    {
                        Step = $"Processing statement {i + 1} of {statements.Count}...",
                        Percent = pct,
                        CurrentStatement = statements[i]
                    });

                    var imagePath = Path.Combine(jobDir, $"img_{i:D3}.png");
                    var audioPath = Path.Combine(jobDir, $"audio_{i:D3}.mp3");

                    var imageTask = GenerateImageThrottledAsync(prompts[i], imagePath, i, statements.Count, progress);
                    var audioTask = GenerateAudioWithEdgeTtsAsync(statements[i], audioPath, voice);
                    await Task.WhenAll(imageTask, audioTask);

                    var duration = await GetAudioDurationAsync(audioPath);
                    totalDurationAccumulator += duration;

                    var clipPath = Path.Combine(jobDir, $"clip_{i:D3}.mp4");
                    await MergeToClipAsync(imagePath, audioPath, duration, clipPath);
                    clipPaths.Add(clipPath);
                }

                _logger.LogInformation("Total accumulated clip duration: {Total:F2}s", totalDurationAccumulator);

                progress.Report(new VideoProgress { Step = "Merging final video...", Percent = 92 });
                var finalPath = Path.Combine(jobDir, "final_video.mp4");
                await ConcatenateClipsAsync(clipPaths, jobDir, finalPath);

                progress.Report(new VideoProgress { Step = "Done!", Percent = 100 });

                return new VideoResult
                {
                    Success = true,
                    VideoUrl = $"/output/{jobId}/final_video.mp4",
                    JobId = jobId,
                    StyleUsed = selectedStyle.Name,
                    StyleId = selectedStyle.Id
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video generation failed for job {JobId}", jobId);
                return new VideoResult { Success = false, Error = ex.Message };
            }
        }

        private List<string> SplitScript(string script)
        {
            return script
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 3)
                .ToList();
        }

        private const int PromptBatchSize = 30;

        // ============================================================
        // Image Prompt Generation
        // ============================================================

        private async Task<List<string>> GenerateImagePromptsAsync(List<string> statements, VideoStyle style)
        {
            var allPrompts = new List<string>();
            var batches = statements
                .Select((s, i) => (s, i))
                .GroupBy(x => x.i / PromptBatchSize)
                .Select(g => g.Select(x => x.s).ToList())
                .ToList();

            for (int b = 0; b < batches.Count; b++)
            {
                var batch = batches[b];
                _logger.LogInformation("Generating image prompts: batch {B}/{Total} ({Count} lines)", b + 1, batches.Count, batch.Count);
                var batchPrompts = await GeneratePromptBatchAsync(batch, statements.Count, style);
                allPrompts.AddRange(batchPrompts);
            }
            return allPrompts;
        }

        // ============================================================
        // FIXED: CORRECT GROQ MODEL ?
        // ============================================================

        private async Task<List<string>> GeneratePromptBatchAsync(List<string> batchStatements, int totalCount, VideoStyle style)
        {
            var groqKey = _config["Groq:ApiKey"] ?? throw new Exception("Groq:ApiKey is not configured.");
            var statementsText = string.Join("\n", batchStatements.Select((s, i) => $"{i + 1}. {s}"));

            var fullPrompt = $@"You are writing SHORT scene descriptions for a faith/wisdom YouTube Shorts channel.

For each statement below, describe ONLY the subject: what the single faceless character is doing (pose, gesture, facing direction), and up to 2 simple symbolic props or setting details (e.g. ""kneeling beside a closed door"", ""standing at the base of a mountain path, arms raised"", ""sitting alone on a bench looking down""). 

The visual style for all images is: {style.Description} ({style.Name} style)

Do NOT mention art style, background color, or rendering technique — that is handled separately. Do NOT literally depict abstract concepts; represent them symbolically through pose and simple props (e.g. sin/temptation -> a figure turning away from a closed door; conflict -> a storm cloud overhead; hope -> a figure looking toward a light beam).

Safety rules for every scene (content is auto-rejected otherwise):
- No nudity, no romantic or physically intimate contact, no violence, no weapons, no blood, no gore.
- Keep every scene modest, calm, and safe-for-work.

Statements:
{statementsText}

Return ONLY a JSON array of short scene phrases like: [""scene1"", ""scene2"", ...]
The array must have exactly {batchStatements.Count} items.";

            var url = "https://api.groq.com/openai/v1/chat/completions";
            var body = new
            {
                // ? FIXED: Using working Groq model
                model = "llama-3.1-8b-instant",
                messages = new[] { new { role = "user", content = fullPrompt } },
                temperature = 0.7,
                max_tokens = 2048
            };

            try
            {
                var json = await CallGroqWithRetryAsync(url, body, groqKey);
                var doc = JsonDocument.Parse(json);
                var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;

                text = text.Trim();
                if (text.StartsWith("```"))
                    text = string.Join("\n", text.Split('\n').Skip(1).TakeWhile(l => !l.Trim().StartsWith("```")));

                int startIndex = text.IndexOf('[');
                int endIndex = text.LastIndexOf(']');

                if (startIndex != -1 && endIndex != -1 && endIndex > startIndex)
                    text = text.Substring(startIndex, endIndex - startIndex + 1);
                else
                    throw new Exception($"Groq did not return a valid JSON array. Received: {text}");

                var scenes = JsonSerializer.Deserialize<List<string>>(text)!;

                while (scenes.Count < batchStatements.Count)
                {
                    scenes.Add("standing calmly, hands relaxed at their sides");
                }
                if (scenes.Count > batchStatements.Count)
                    scenes = scenes.Take(batchStatements.Count).ToList();

                return scenes.Select(scene => $"{style.StylePrefix}{scene}{style.StyleSuffix}").ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate prompts with Groq");
                throw;
            }
        }

        private async Task<string> CallGroqWithRetryAsync(string url, object body, string apiKey)
        {
            Exception? lastError = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Headers.Add("Authorization", $"Bearer {apiKey}");
                    req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var res = await _http.SendAsync(req, cts.Token);
                    var json = await res.Content.ReadAsStringAsync();

                    if (res.IsSuccessStatusCode) return json;

                    if ((int)res.StatusCode == 429 || (int)res.StatusCode >= 500)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                        _logger.LogWarning("Groq API returned {Status}, retry {Attempt}/{Max} after {Delay}s", (int)res.StatusCode, attempt, MaxRetries, delay.TotalSeconds);
                        lastError = new Exception($"Groq API error ({res.StatusCode}): {json}");
                        if (attempt < MaxRetries) { await Task.Delay(delay); continue; }
                        throw lastError;
                    }

                    throw new Exception($"Groq API error ({res.StatusCode}): {json}");
                }
                catch (Exception ex) when (
                    ex is TaskCanceledException ||
                    ex is OperationCanceledException ||
                    ex is IOException ||
                    ex is System.Net.Sockets.SocketException ||
                    ex is HttpRequestException)
                {
                    lastError = ex;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogWarning("Groq API network error attempt {Attempt}/{Max}: {Message}, retrying after {Delay}s", attempt, MaxRetries, ex.Message, delay.TotalSeconds);
                    if (attempt < MaxRetries) await Task.Delay(delay);
                }
            }
            throw new Exception($"Groq API failed after all retries: {lastError?.Message}", lastError);
        }

        // ============================================================
        // Audio Generation
        // ============================================================

        private async Task GenerateAudioWithEdgeTtsAsync(string text, string outputPath, string voice)
        {
            if (!_voiceOptions.ContainsKey(voice))
                voice = "en-US-AriaNeural";

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                var tmpText = Path.GetTempFileName();
                var tmpScript = Path.GetTempFileName() + ".py";

                try
                {
                    await File.WriteAllTextAsync(tmpText, text);

                    var pythonScript =
                        "import asyncio\n" +
                        "import edge_tts\n" +
                        "\n" +
                        "async def run():\n" +
                        $"    with open(r\"{tmpText}\", \"r\", encoding=\"utf-8\") as f:\n" +
                        "        text = f.read()\n" +
                        $"    communicate = edge_tts.Communicate(text, \"{voice}\", rate=\"{TtsSpeechRate}\", volume=\"+10%\")\n" +
                        $"    await communicate.save(r\"{outputPath}\")\n" +
                        "\n" +
                        "asyncio.run(run())\n";

                    await File.WriteAllTextAsync(tmpScript, pythonScript);
                    await RunProcessAsync("python", $"\"{tmpScript}\"", timeoutSeconds: 45);

                    if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                        throw new Exception("Edge TTS failed to generate audio (file is missing or empty).");

                    await TrimSilenceAsync(outputPath);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == MaxRetries)
                        throw new Exception($"Edge TTS failed after {MaxRetries} attempts: {ex.Message}", ex);

                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogWarning("Edge TTS attempt {Attempt}/{Max} failed: {Message}, retrying after {Delay}s", attempt, MaxRetries, ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
                finally
                {
                    SafeDelete(tmpText);
                    SafeDelete(tmpScript);
                }
            }
        }

        // ============================================================
        // Image Generation
        // ============================================================

        private async Task GenerateImageThrottledAsync(string prompt, string outputPath, int index, int total, IProgress<VideoProgress> progress)
        {
            await _imageGenLock.WaitAsync();
            try
            {
                var elapsed = (DateTime.UtcNow - _lastImageGenCall).TotalMilliseconds;
                var wait = ImageGenMinIntervalMs - elapsed;
                if (wait > 0)
                {
                    progress.Report(new VideoProgress
                    {
                        Step = $"Waiting {Math.Ceiling(wait / 1000.0)}s to avoid rate limits — image {index + 1} of {total}...",
                        Percent = 15 + (index * 75 / total)
                    });
                    await Task.Delay((int)wait);
                }
                _lastImageGenCall = DateTime.UtcNow;
            }
            finally { _imageGenLock.Release(); }
            await GenerateImageAsync(prompt, outputPath, progress, index, total);
        }

        private async Task GenerateImageAsync(
            string originalPrompt, string outputPath, IProgress<VideoProgress> progress, int index, int total)
        {
            var accountId = _config["Cloudflare:AccountId"] ?? throw new Exception("Cloudflare:AccountId is not configured.");
            var apiToken = _config["Cloudflare:ApiToken"] ?? throw new Exception("Cloudflare:ApiToken is not configured.");
            var model = "@cf/black-forest-labs/flux-1-schnell";
            var url = $"https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/{model}";

            var promptQueue = new List<string>
            {
                originalPrompt,
                SoftenPrompt(originalPrompt),
                "A simple bright stick-figure doodle on a plain white background, thin black ink lines, single character standing calmly, hands relaxed, no text in image",
                "A minimal bright ink doodle of a person sitting quietly on a plain white background, calm and reflective mood, no text in image",
                "A simple bright ink line drawing of a person with hands folded gently in prayer, plain white paper background, peaceful pose, no text in image",
                "A gentle bright ink doodle of a person looking up hopefully, plain white background, thin uniform lines, no text in image"
            };

            Exception? lastError = null;
            const int MaxTotalAttempts = 10;
            int promptIndex = 0;

            for (int attempt = 1; attempt <= MaxTotalAttempts; attempt++)
            {
                var currentPrompt = promptQueue[Math.Min(promptIndex, promptQueue.Count - 1)];

                try
                {
                    var body = new { prompt = currentPrompt };

                    var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Headers.Add("Authorization", $"Bearer {apiToken}");
                    req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var res = await _http.SendAsync(req, cts.Token);

                    if (res.IsSuccessStatusCode)
                    {
                        var json = await res.Content.ReadAsStringAsync();
                        var doc = JsonDocument.Parse(json);

                        if (doc.RootElement.TryGetProperty("result", out var resultElement))
                        {
                            if (resultElement.TryGetProperty("image", out var imageElement))
                            {
                                var b64 = imageElement.GetString()!;
                                await File.WriteAllBytesAsync(outputPath, Convert.FromBase64String(b64));
                                if (promptIndex > 0)
                                    _logger.LogInformation("Scene {Index}: succeeded using fallback prompt #{PromptIndex}.", index + 1, promptIndex);
                                return;
                            }
                        }

                        lastError = new Exception($"Cloudflare returned success but no image data.");
                        _logger.LogWarning("Scene {Index}: No image data in response", index + 1);
                        continue;
                    }

                    var errorText = await res.Content.ReadAsStringAsync();
                    _logger.LogWarning("Scene {Index}: Cloudflare image gen failed. Status={Status}", index + 1, res.StatusCode);

                    if ((int)res.StatusCode == 401 || (int)res.StatusCode == 403)
                    {
                        lastError = new Exception($"Cloudflare auth error ({res.StatusCode}). Check API token permissions.");
                        break;
                    }

                    if ((int)res.StatusCode == 429)
                    {
                        var waitTime = TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, attempt - 1)));
                        _logger.LogWarning("Scene {Index}: Rate limited. Waiting {WaitTime:F0}s", index + 1, waitTime.TotalSeconds);
                        progress.Report(new VideoProgress
                        {
                            Step = $"Scene {index + 1}/{total}: Rate limited, waiting {waitTime.TotalSeconds:F0}s...",
                            Percent = 15 + (index * 75 / total)
                        });
                        await Task.Delay(waitTime);
                        continue;
                    }

                    if ((int)res.StatusCode == 400 && errorText.Contains("NSFW", StringComparison.OrdinalIgnoreCase))
                    {
                        lastError = new Exception($"NSFW rejection on prompt #{promptIndex}");
                        progress.Report(new VideoProgress
                        {
                            Step = $"Scene {index + 1}/{total}: adjusting image prompt and retrying...",
                            Percent = 15 + (index * 75 / total)
                        });
                        promptIndex++;
                        continue;
                    }

                    if ((int)res.StatusCode >= 500)
                    {
                        lastError = new Exception($"Cloudflare server error ({res.StatusCode})");
                        var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                        await Task.Delay(delay);
                        continue;
                    }

                    lastError = new Exception($"Cloudflare image generation failed ({res.StatusCode})");
                }
                catch (Exception ex) when (
                    ex is TaskCanceledException ||
                    ex is OperationCanceledException ||
                    ex is IOException ||
                    ex is System.Net.Sockets.SocketException ||
                    ex is HttpRequestException)
                {
                    lastError = ex;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))));
                }
            }

            _logger.LogError("Scene {Index}: ALL attempts exhausted — falling back to blank placeholder.", index + 1);
            await GenerateLocalPlaceholderImageAsync(outputPath);
        }

        private static string SoftenPrompt(string prompt)
        {
            var wordsToStrip = new[]
            {
                "embracing", "embrace", "hugging", "hug", "intimate", "touching", "touch",
                "caress", "kissing", "kiss", "naked", "nude", "bare", "seductive",
                "bleeding", "blood", "wound", "weapon", "gun", "knife", "fighting", "violence"
            };

            var result = prompt;
            foreach (var word in wordsToStrip)
                result = System.Text.RegularExpressions.Regex.Replace(result, $@"\b{word}\b", "standing calmly", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return result + ", modest, safe-for-work, gentle pose";
        }

        private async Task GenerateLocalPlaceholderImageAsync(string outputPath)
        {
            var args = $"-f lavfi -i color=c=0xF5EFE0:s={ImageWidth}x{ImageHeight} -frames:v 1 -y \"{outputPath}\"";
            await RunProcessAsync("ffmpeg", args, timeoutSeconds: 30);
        }

        // ============================================================
        // Audio Processing
        // ============================================================

        private async Task TrimSilenceAsync(string audioPath)
        {
            var trimmedPath = audioPath + ".trimmed.mp3";
            try
            {
                var args = $"-i \"{audioPath}\" -af \"{SilenceTrimFilter}\" -y \"{trimmedPath}\"";
                await RunProcessAsync("ffmpeg", args, timeoutSeconds: 30);

                if (File.Exists(trimmedPath) && new FileInfo(trimmedPath).Length > 0)
                {
                    File.Delete(audioPath);
                    File.Move(trimmedPath, audioPath);
                }
                else
                {
                    SafeDelete(trimmedPath);
                }
            }
            catch
            {
                SafeDelete(trimmedPath);
            }
        }

        private async Task<double> GetAudioDurationAsync(string audioPath)
        {
            var output = await RunProcessAsync("ffprobe",
                $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"",
                returnOutput: true);
            if (double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var dur))
            {
                return dur;
            }
            return 5.0;
        }

        // ============================================================
        // Video Processing
        // ============================================================

        private async Task MergeToClipAsync(string imagePath, string audioPath, double duration, string outputPath)
        {
            var totalFrames = (int)Math.Ceiling(duration * OutputFps);
            var zoomPerFrame = 0.15 / Math.Max(totalFrames, 1);
            var zoomExpr = $"min(1+{zoomPerFrame.ToString("G17", CultureInfo.InvariantCulture)}*on,1.15)";

            const int UpscaleFactor = 6;
            var vf =
                $"scale={ImageWidth}:{ImageHeight}:force_original_aspect_ratio=increase," +
                $"crop={ImageWidth}:{ImageHeight}," +
                $"scale={ImageWidth * UpscaleFactor}:{ImageHeight * UpscaleFactor}:flags=lanczos," +
                $"zoompan=z='{zoomExpr}':d={totalFrames}:x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s={ImageWidth}x{ImageHeight}:fps={OutputFps}";

            var durStr = duration.ToString("F2", CultureInfo.InvariantCulture);

            var args = $"-loop 1 -i \"{imagePath}\" -i \"{audioPath}\" " +
                       $"-vf \"{vf}\" " +
                       $"-c:v libx264 -preset fast -t {durStr} -pix_fmt yuv420p -c:a aac -shortest \"{outputPath}\"";

            await RunProcessAsync("ffmpeg", args, timeoutSeconds: 120);
        }

        private async Task ConcatenateClipsAsync(List<string> clipPaths, string jobDir, string outputPath)
        {
            var inputs = string.Join(" ", clipPaths.Select(p => $"-i \"{p}\""));
            var concatInput = string.Concat(
                Enumerable.Range(0, clipPaths.Count).Select(i => $"[{i}:v][{i}:a]"));
            var filter = $"{concatInput}concat=n={clipPaths.Count}:v=1:a=1[outv][outa]";

            var args =
                $"{inputs} " +
                $"-filter_complex \"{filter}\" " +
                $"-map \"[outv]\" -map \"[outa]\" " +
                $"-c:v libx264 -preset fast -crf 22 -c:a aac \"{outputPath}\"";

            await RunProcessAsync("ffmpeg", args, timeoutSeconds: 300);
        }

        // ============================================================
        // Process Runner
        // ============================================================

        private async Task<string> RunProcessAsync(string exe, string args,
            int timeoutSeconds = 60, bool returnOutput = false)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)
                ?? throw new Exception($"Failed to start process '{exe}'.");
            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var errorTask = proc.StandardError.ReadToEndAsync();
            var completed = await Task.Run(() => proc.WaitForExit(timeoutSeconds * 1000));
            if (!completed) { proc.Kill(true); throw new Exception($"{exe} timed out after {timeoutSeconds}s"); }
            var output = await outputTask;
            var error = await errorTask;
            if (proc.ExitCode != 0)
            {
                _logger.LogError("{Exe} exited with code {Code}: {Error}", exe, proc.ExitCode, error);
                throw new Exception($"{exe} failed (exit code {proc.ExitCode}): {error}");
            }
            return returnOutput ? output : string.Empty;
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}