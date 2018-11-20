
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Loader;
using System.Text;
using System.Threading;

namespace Refiner
{
    class Program
    {
        private static IConnection conn;

        private static IModel channel;
        private static readonly AutoResetEvent WaitHandle = new AutoResetEvent(false);

        static void Main(string[] args)
        {
            AssemblyLoadContext.Default.Unloading += _ => Exit();
            Console.CancelKeyPress += (_, __) => Exit();

            Log.Logger = new LoggerConfiguration()
                .Enrich.WithProperty("name", typeof(Program).Assembly.GetName().Name)
                .WriteTo.Console()
                .CreateLogger();

            Log.Information("Starting...");

            var factory = new ConnectionFactory() { HostName = "rabbit.docker" };
            conn = factory.CreateConnection();
            channel = conn.CreateModel();

            var exchange = "grabid_exchange";
            var routingKey = "mono.data.received";

            channel.ExchangeDeclare(exchange: exchange, type: "topic");

            var queueName = channel.QueueDeclare().QueueName;

            channel.QueueBind(queue: queueName,
                                  exchange: exchange,
                                  routingKey: routingKey);


            var consumer = new EventingBasicConsumer(channel);

            consumer.Received += (model, ea) =>
            {
                var body = ea.Body;
                var message = Encoding.UTF8.GetString(body);
                var key = ea.RoutingKey;
                Console.WriteLine($" [x] Received '{key}':'{message}'");
                var envelope = JsonConvert.DeserializeObject<Envelope<string>>(message);

                dynamic json = JObject.Parse(envelope.Payload);
                string messageString = json.message;
                string userString = json.users;
                string[] userArray = userString.Split(",");
                string[] messages = userArray.Select(user => { return $"{messageString} {user}"; }).ToArray();
                var returnEnvelope = new Envelope<string[]>(envelope.Id, messages);
                string newMessage = JsonConvert.SerializeObject(returnEnvelope);
                byte[] messageBodyBytes = Encoding.UTF8.GetBytes(newMessage);

                channel.BasicPublish(exchange, "mono.data.refined", null, messageBodyBytes);
                Console.WriteLine($" [x] Sent 'mono.data.refined':'{newMessage}'");

            };

            channel.BasicConsume(queue: queueName,
                                 autoAck: true,
                                 consumer: consumer);

            Log.Information("Started");

            WaitHandle.WaitOne();
        }

        private static void Exit()
        {
            Log.Information("Exiting...");
            channel.Close();
            conn.Close();

           
        }

       
    }

}
