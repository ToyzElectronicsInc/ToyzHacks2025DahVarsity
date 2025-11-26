using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
public record IncomingMessageDto(string RoomId, string UserId, string Text, string AudioUrl = null);
public record RemoveMessageDto(string MessageId, string Reason);
public class MessageRecord
{
    public string MessageId { get; set; }
    public string RoomId { get; set; }
    public string UserId { get; set; }
    public string Text { get; set; }
    public string AudioUrl { get; set; }
    public string Status { get; set; }
    public string ModerationJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
}