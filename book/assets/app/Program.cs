// Минимальное API: заказ → PostgreSQL + RabbitMQ.
// Глава 5 книги: приложение запускается на ХОСТЕ, Postgres и RabbitMQ — в контейнерах.
// Всё, что «настраивается», приходит из переменных окружения (12-factor).
using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var builder = WebApplication.CreateBuilder(args);

static string Env(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

string pgConn    = Env("PG_CONN", "Host=localhost;Port=5432;Username=app;Password=app;Database=appdb");
string amqpUri   = Env("AMQP_URI", "amqp://app:app@localhost:5672/");
string queueName = Env("QUEUE_NAME", "orders");

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(pgConn));
builder.Services.AddSingleton(new RabbitOptions(new Uri(amqpUri), queueName));
builder.Services.AddSingleton<OrdersConsumer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OrdersConsumer>());

var app = builder.Build();

// --- схема БД создаётся на старте: в лабе удобно, в проде этим занимаются миграции ---
await using (var conn = await app.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync())
await using (var cmd = new NpgsqlCommand(
    "create table if not exists orders (" +
    "id bigserial primary key, " +
    "item text not null, " +
    "qty int not null, " +
    "status text not null default 'new', " +
    "created_at timestamptz not null default now())", conn))
{
    await cmd.ExecuteNonQueryAsync();
}

// 1. Живость: приложение + БД + брокер
app.MapGet("/health", async (NpgsqlDataSource ds, OrdersConsumer consumer) =>
{
    string dbState = "ok", mqState = consumer.IsConnected ? "ok" : "fail: нет соединения с RabbitMQ";
    try
    {
        await using var conn = await ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("select 1", conn);
        await cmd.ExecuteScalarAsync();
    }
    catch (Exception e) { dbState = "fail: " + e.Message; }

    bool healthy = dbState == "ok" && mqState == "ok";
    return Results.Json(new { status = healthy ? "healthy" : "degraded", db = dbState, mq = mqState },
                        statusCode: healthy ? 200 : 503);
});

// 2. Создать заказ: пишем в PostgreSQL и публикуем сообщение в очередь
app.MapPost("/orders", async (OrderRequest req, NpgsqlDataSource ds, RabbitOptions opts) =>
{
    long id;
    await using (var conn = await ds.OpenConnectionAsync())
    await using (var cmd = new NpgsqlCommand(
        "insert into orders (item, qty) values (@item, @qty) returning id", conn))
    {
        cmd.Parameters.AddWithValue("item", req.Item);
        cmd.Parameters.AddWithValue("qty", req.Qty);
        id = (long)(await cmd.ExecuteScalarAsync())!;
    }

    var (connection, channel) = await RabbitChannel.OpenAsync(opts);
    await using (connection)
    await using (channel)
    {
        await channel.BasicPublishAsync(exchange: string.Empty, routingKey: opts.Queue,
                                        body: JsonSerializer.SerializeToUtf8Bytes(new { id, req.Item }));
    }

    return Results.Created($"/orders/{id}", new { id, status = "new" });
});

// 3. Прочитать последние заказы
app.MapGet("/orders", async (NpgsqlDataSource ds) =>
{
    var rows = new List<object>();
    await using var conn = await ds.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        "select id, item, qty, status from orders order by id desc limit 20", conn);
    await using var rdr = await cmd.ExecuteReaderAsync();
    while (await rdr.ReadAsync())
        rows.Add(new { id = rdr.GetInt64(0), item = rdr.GetString(1), qty = rdr.GetInt32(2), status = rdr.GetString(3) });
    return Results.Ok(rows);
});

app.Run();

// ---------------------------------------------------------------------------

record OrderRequest(string Item, int Qty);
record RabbitOptions(Uri Uri, string Queue);

// Соединение и канал поднимаются на каждый запрос: в лабе просто,
// в проде держат один долгоживущий канал или клиент с пулом.
static class RabbitChannel
{
    public static async Task<(IConnection Connection, IChannel Channel)> OpenAsync(RabbitOptions opts)
    {
        var factory = new ConnectionFactory { Uri = opts.Uri };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue: opts.Queue, durable: true, exclusive: false, autoDelete: false);
        return (connection, channel);
    }
}

// Фоновый потребитель: читает очередь и переводит заказ в статус 'processed'
sealed class OrdersConsumer(NpgsqlDataSource ds, RabbitOptions opts, ILogger<OrdersConsumer> log)
    : BackgroundService
{
    private IConnection? _connection;
    public bool IsConnected => _connection is { IsOpen: true };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory { Uri = opts.Uri };
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(factory, ct);
            }
            catch (Exception e)
            {
                log.LogWarning("RabbitMQ недоступен ({Message}), повтор через 5 секунд", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ConnectAndConsumeAsync(ConnectionFactory factory, CancellationToken ct)
    {
        if (_connection is not null) await _connection.DisposeAsync();

        _connection = await factory.CreateConnectionAsync(ct);
        var channel = await _connection.CreateChannelAsync(cancellationToken: ct);
        await channel.QueueDeclareAsync(opts.Queue, durable: true, exclusive: false,
                                        autoDelete: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            long id = JsonDocument.Parse(ea.Body.ToArray()).RootElement.GetProperty("id").GetInt64();
            await using (var conn = await ds.OpenConnectionAsync(ct))
            await using (var cmd = new NpgsqlCommand("update orders set status = 'processed' where id = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            log.LogInformation("Заказ {Id} обработан", id);
        };

        await channel.BasicConsumeAsync(opts.Queue, autoAck: false, consumer: consumer, cancellationToken: ct);
        log.LogInformation("Слушаю очередь {Queue}", opts.Queue);

        // ждём, пока соединение не закроется (упал брокер, перезапуск контейнера и т.п.),
        // и уходим на переподключение во внешнем цикле
        var closed = new TaskCompletionSource();
        _connection.ConnectionShutdownAsync += (_, args) =>
        {
            log.LogWarning("Соединение с RabbitMQ закрыто: {Reason}", args.ReplyText);
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await closed.Task;
    }
}
