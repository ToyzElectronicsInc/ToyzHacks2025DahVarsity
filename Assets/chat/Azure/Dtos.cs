// Dtos.cs — Unity-compatible version
using System.Collections.Generic;

namespace Chat.SharedDtos
{
    public class IncomingMessageDto
    {
        public string RoomId { get; set; }
        public string UserId { get; set; }
        public string Text { get; set; }
        public string AudioUrl { get; set; }
    }

    public class RemoveMessageDto
    {
        public string MessageId { get; set; }
        public string Reason { get; set; }
    }

    public class ModerationResultDto
    {
        public string Action { get; set; }
        public double OverallScore { get; set; }
        public Dictionary<string, CategoryDto> Categories { get; set; } = new Dictionary<string, CategoryDto>();
        public string Explanation { get; set; }
        public string[] Evidence { get; set; } = System.Array.Empty<string>();
    }

    public class CategoryDto
    {
        public string Label { get; set; }
        public double Score { get; set; }
    }

    public class ModerationMetaDto
    {
        public string Action { get; set; }
        public double Score { get; set; }
        public string Explanation { get; set; }
    }
}
