using System.Threading.Tasks;
using Jifas.Assistant.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jifas.Assistant.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackLearningService _feedbackLearning;
    private readonly IAiLearningService _aiLearning;
    private readonly ILoggerService _logger;

    public FeedbackController(
        IFeedbackLearningService feedbackLearning,
        IAiLearningService aiLearning,
        ILoggerService logger)
    {
        _feedbackLearning = feedbackLearning;
        _aiLearning = aiLearning;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> RecordFeedback([FromBody] FeedbackRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "Feedback payload is required" });

        if (request.Rating < 1 || request.Rating > 5)
            return BadRequest(new { error = "Rating must be between 1 and 5" });

        await _feedbackLearning.RecordFeedbackAsync(new UserFeedbackInput
        {
            ChatId = request.ChatId,
            SessionId = request.SessionId ?? string.Empty,
            MessageId = request.MessageId ?? string.Empty,
            UserId = request.UserId ?? "anonymous",
            Rating = request.Rating,
            Comment = request.Comment ?? string.Empty
        });

        LearningCandidateDto? candidate = null;
        if (request.ChatId.HasValue)
        {
            try
            {
                candidate = await _aiLearning.CreateCandidateFromFeedbackAsync(
                    request.ChatId.Value,
                    request.Rating,
                    request.Comment);
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning("[AiLearning] Feedback saved, but candidate creation failed: {Message}", ex.Message);
            }
        }

        return Ok(new
        {
            success = true,
            learningCandidateId = candidate?.Id,
            learningStatus = candidate?.Status
        });
    }
}

public class FeedbackRequest
{
    public int? ChatId { get; set; }
    public string? SessionId { get; set; }
    public string? MessageId { get; set; }
    public string? UserId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
}
