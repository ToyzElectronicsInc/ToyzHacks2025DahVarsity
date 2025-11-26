using System;
using NUnit.Framework;

[TestFixture]
public class ChatMessagePayloadTests
{
    [Test]
    public void Create_SetsAllFieldsAndGeneratesMessageId()
    {
        // Arrange
        string senderId = "user123";
        string senderName = "Alice";
        string roomId = "room42";
        string text = "Hello world";
        var mod = new ModerationMeta { Action = "warn", Score = 0.7, Explanation = "Test moderation" };
        string audioUrl = "https://example.com/audio.wav";

        // Act
        var msg = ChatMessagePayload.Create(senderId, senderName, roomId, text, mod, audioUrl);

        // Assert
        Assert.IsNotNull(msg, "Message object should not be null");
        Assert.IsFalse(string.IsNullOrEmpty(msg.MessageId), "MessageId should be auto-generated");
        Assert.AreEqual(senderId, msg.SenderId);
        Assert.AreEqual(senderName, msg.SenderName);
        Assert.AreEqual(roomId, msg.RoomId);
        Assert.AreEqual(text, msg.Text);
        Assert.AreEqual(audioUrl, msg.AudioUrl);
        Assert.AreSame(mod, msg.ModerationMeta);

        // Validate timestamp format (ISO 8601 / roundtrip)
        Assert.DoesNotThrow(() =>
        {
            DateTime parsed = DateTime.Parse(msg.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind);
        }, "Timestamp should be a valid ISO 8601 date string");
    }

    [Test]
    public void Create_AllowsNullOptionalParameters()
    {
        // Act
        var msg = ChatMessagePayload.Create("id", "name", "room", "message");

        // Assert
        Assert.IsNotNull(msg);
        Assert.IsNull(msg.ModerationMeta);
        Assert.IsNull(msg.AudioUrl);
    }
}