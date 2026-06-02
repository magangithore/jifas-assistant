using System.Threading.Tasks;
using Jifas.Assistant.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jifas.Assistant.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackLearningService _feedbackLearning;

    public FeedbackController(IFeedbackLearningService feedbackLearning)
    {
        _feedbackLearning = feedbackLearning;
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

        return Ok(new { success = true });
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
