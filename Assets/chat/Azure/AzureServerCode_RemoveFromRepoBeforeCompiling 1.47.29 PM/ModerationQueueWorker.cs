using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
public class ModerationQueueWorker : IHostedService, IAsyncDisposable
{
    private readonly ServiceBusProcessor _processor;
    private readonly IModerationService _moderation;
    private readonly IMessageRepository _repo;
    private readonly IHttpClientFactory _http;
    private readonly ILogger _log;
    private readonly string _gatewayRemoveEndpoint; // e.g. https://gateway.example.com/api/messages/remove

    public ModerationQueueWorker(ServiceBusClient client, string queueName, IModerationService moderation,
                                 IMessageRepository repo, IHttpClientFactory httpFactory, ILogger logger, string gatewayRemoveEndpoint)
    {
        _moderation = moderation; _repo = repo; _http = httpFactory; _log = logger; _gatewayRemoveEndpoint = gatewayRemoveEndpoint;
        _processor = client.CreateProcessor(queueName, new ServiceBusProcessorOptions { MaxConcurrentCalls = 4 });
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ErrorHandler;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _processor.StartProcessingAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => _processor.StopProcessingAsync(cancellationToken);

    private Task ErrorHandler(ProcessErrorEventArgs e) { _log.LogError(e.Exception, "Service Bus error"); return Task.CompletedTask; }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs msgArgs)
    {
        var body = msgArgs.Message.Body.ToString();
        var q = JsonSerializer.Deserialize<QueueMessage>(body);
        if (q == null) { await msgArgs.CompleteMessageAsync(msgArgs.Message); return; }

        var rec = await _repo.GetMessageAsync(q.MessageId);
        if (rec == null) { await msgArgs.CompleteMessageAsync(msgArgs.Message); return; }

        // gather context and call full moderation
        var context = (await _repo.GetRecentMessages(rec.RoomId, 20)).Select(m => new { m.Text, m.UserId, m.MessageId }).ToArray();
        var fullMod = await _moderation.FullModerateAsync(rec.Text, context, rec.UserId, rec.AudioUrl);

        if (fullMod != null && (string.Equals(fullMod.Action, "block", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(fullMod.Action, "escalate", StringComparison.OrdinalIgnoreCase)))
        {
            // update db
            rec.Status = "removed";
            rec.ModerationJson = JsonSerializer.Serialize(fullMod);
            rec.RemovedAt = DateTime.UtcNow;
            await _repo.UpdateMessageAsync(rec);

            // tell gateway to remove (authorized call; authenticate)
            var http = _http.CreateClient("gateway");
            var payload = new { MessageId = rec.MessageId, Reason = fullMod.Explanation };
            var r = await http.PostAsJsonAsync(_gatewayRemoveEndpoint, payload);

            // push to moderator queue or create incident (omitted)
        }
        else
        {
            // save full moderation results
            rec.ModerationJson = JsonSerializer.Serialize(fullMod);
            await _repo.UpdateMessageAsync(rec);
        }

        await msgArgs.CompleteMessageAsync(msgArgs.Message);
    }

    public async ValueTask DisposeAsync() { await _processor.DisposeAsync(); }
}