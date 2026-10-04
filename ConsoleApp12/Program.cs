using System;
using System.Text.Json;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;

var sqs = new AmazonSQSClient("AKIA6M6LKSIHYWGELMF7", "NEFTtLJvQ1F3G6Vr4R9Yz4eCuK3XBsKzEz8hP2t+", RegionEndpoint.EUCentral1);
var queueUrl = "https://sqs.eu-central-1.amazonaws.com/989879505423/sqsexample1";

while (true)
{
    Console.WriteLine("1 - Создать заявку | 2 - Обработать все заявки | 3 - Показать примерное количество | 0 - Выход");
    Console.Write("Выбор: ");
    var key = Console.ReadLine();
    if (key == "0") break;

    if (key == "1")
    {
        Console.WriteLine("Приоритет (Low/Medium/High) [Low]: ");
        var pinput = Console.ReadLine() ?? string.Empty;
        string priority = pinput.Trim().ToLower() switch
        {
            "h" or "high" => "High",
            "m" or "medium" => "Medium",
            "l" or "low" => "Low",
            _ => "Low"
        };

        var req = new SupportRequest
        {
            Id = Guid.NewGuid().ToString(),
            UserName = Prompt("Имя пользователя"),
            Topic = Prompt("Тема"),
            Description = Prompt("Описание"),
            Priority = priority,
            CreatedAt = DateTime.UtcNow
        };

        var body = JsonSerializer.Serialize(req);
        await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queueUrl, MessageBody = body });
        Console.WriteLine($"Отправлено: {req.Id} (Priority: {req.Priority})");
        Console.WriteLine("Нажмите Enter для возврата в меню...");
        Console.ReadLine();
    }
    else if (key == "2")
    {
        Console.WriteLine("Обработка очереди...");
        try
        {
            while (true)
            {
                var resp = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 5
                });

                if (resp.Messages == null || resp.Messages.Count == 0)
                {
                    Console.WriteLine("Очередь пуста.");
                    break;
                }

                foreach (var m in resp.Messages)
                {
                    try
                    {
                        SupportRequest r = null;
                        try
                        {
                            using var doc = JsonDocument.Parse(m.Body);
                            var root = doc.RootElement;

                            var map = new System.Collections.Generic.Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                            if (root.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var prop in root.EnumerateObject())
                                    map[prop.Name] = prop.Value;
                            }

                            static string GetStringFromElement(JsonElement e)
                            {
                                return e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
                            }

                            string GetAny(params string[] names)
                            {
                                foreach (var n in names)
                                {
                                    if (map.TryGetValue(n, out var el))
                                        return GetStringFromElement(el);
                                }
                                return null;
                            }

                            var id = GetAny("Id", "id");
                            var user = GetAny("UserName", "userName", "username", "CustomerName", "customerName", "customer", "name", "Customer");
                            var topic = GetAny("Topic", "topic", "PizzaType", "pizzatype", "type");
                            var desc = GetAny("Description", "description", "details");
                            var priority = GetAny("Priority", "priority");
                            var createdRaw = GetAny("CreatedAt", "createdAt", "OrderDate", "orderDate", "Created", "Timestamp");

                            DateTime? created = null;
                            if (!string.IsNullOrEmpty(createdRaw))
                            {
                                if (long.TryParse(createdRaw.Trim('"'), out var lv))
                                {
                                    try { created = DateTimeOffset.FromUnixTimeSeconds(lv).UtcDateTime; } catch { /* ignore */ }
                                }
                                if (created == null)
                                {
                                    if (DateTime.TryParse(createdRaw.Trim('"'), out var dt))
                                        created = dt.ToUniversalTime();
                                }
                            }

                            r = new SupportRequest { Id = id, UserName = user, Topic = topic, Description = desc, Priority = priority, CreatedAt = created ?? DateTime.MinValue };
                        }
                        catch (JsonException)
                        {
                            r = JsonSerializer.Deserialize<SupportRequest>(m.Body);
                        }

                        if (r != null)
                        {
                            var pri = string.IsNullOrEmpty(r.Priority) ? "Low" : r.Priority;
                            Console.WriteLine($"Id: {r.Id} | User: {r.UserName} | Topic: {r.Topic} | Priority: {pri}");

                            if (r.CreatedAt != DateTime.MinValue)
                            {
                                var waited = DateTime.UtcNow - r.CreatedAt.ToUniversalTime();
                                Console.WriteLine($"Ждала: {waited:g}");
                            }

                            if (pri.Equals("High", StringComparison.OrdinalIgnoreCase))
                                Console.WriteLine("!!! Термінова заявка !!!");

                            Console.WriteLine(r.Description);
                        }

                        await sqs.DeleteMessageAsync(new DeleteMessageRequest { QueueUrl = queueUrl, ReceiptHandle = m.ReceiptHandle });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при обработке сообщения: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при получении сообщений: {ex.Message}");
        }

        Console.WriteLine("Нажмите Enter для возврата в меню...");
        Console.ReadLine();
    }
    else if (key == "3")
    {
        try
        {
            var attrs = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                AttributeNames = new System.Collections.Generic.List<string> { "ApproximateNumberOfMessages" }
            });

            if (attrs.ApproximateNumberOfMessages != null)
                Console.WriteLine($"Примерное количество сообщений: {attrs.ApproximateNumberOfMessages}");
            else
                Console.WriteLine("Не удалось получить размер очереди.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка получения размера очереди: {ex.Message}");
        }

        Console.WriteLine("Нажмите Enter для возврата в меню...");
        Console.ReadLine();
    }
}

static string Prompt(string label)
{
    Console.Write(label + ": ");
    return Console.ReadLine() ?? string.Empty;
}
