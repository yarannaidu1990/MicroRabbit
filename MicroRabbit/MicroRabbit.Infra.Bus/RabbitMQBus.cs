using System.Text;
using System.Text.Json.Serialization;
using MediatR;
using MicroRabbit.Domain.Core.Bus;
using MicroRabbit.Domain.Core.Commands;
using MicroRabbit.Domain.Core.Events;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MicroRabbit.Infra.Bus
{
    public sealed class RabbitMQBus : IEventBus
    {
        private readonly IMediator _mediator;
        private readonly Dictionary<string, List<Type>> _handlers;
        private readonly List<Type> _eventTypes;

        public RabbitMQBus(IMediator mediator)
        {
            _mediator = mediator;
            _handlers = new Dictionary<string, List<Type>>();
            _eventTypes = new List<Type>();
        }
        public Task SendCommand<T>(T command) where T : Command
        {
            return _mediator.Send(command);
        }

        public void Publish<T>(T @event) where T : Event
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                var eventname = @event.GetType().Name;
                channel.QueueDeclare(eventname, false, false, false, null);
                var message = JsonConvert.SerializeObject(eventname);
                var body = Encoding.UTF8.GetBytes(message);
                channel.BasicPublish(null, eventname, null, body);
            }
        }


        public void Subscribe<T, TH>()
            where T : Event
            where TH : IEventHandler<T>
        {
            var eventname = typeof(T).Name;
            var handlertype = typeof(TH);

            if(!_eventTypes.Contains(typeof(T)))
            {
                _eventTypes.Add(typeof(T));
            }
            if(!_handlers.ContainsKey(eventname))
            {
                _handlers.Add(eventname, new List<Type>());
            }
            if (_handlers[eventname].Any(x=>x.GetType() == handlertype))
            {
                throw new ArgumentException($"Handler Type {handlertype.Name} is already registered for {eventname}");
            }

            _handlers[eventname].Add(handlertype);

            StartBasicConsume<T>();
        }

        private void StartBasicConsume<T>() where T : Event
        {
            var factory = new ConnectionFactory() { HostName = "localhost" ,DispatchConsumersAsync = true};
            var connection = factory.CreateConnection();
            var channel = connection.CreateModel();

            var eventname = typeof(T).Name;

            channel.QueueDeclare(eventname, false, false, false, null);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += Consumer_Received;
            channel.BasicConsume(eventname,true,consumer);

        }

        private async Task Consumer_Received(object sender, BasicDeliverEventArgs e)
        {
            var eventname = e.RoutingKey;
            var message = Encoding.UTF8.GetString(e.Body);
            try
            {
                await ProcessEvent(eventname,message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {

            }
        }

        private async Task ProcessEvent(string eventname, string message)
        {
            if(_handlers.ContainsKey(eventname))
            {
                var subscriptions = _handlers[eventname];
                foreach(var subscription in subscriptions)
                {
                    var handler = Activator.CreateInstance(subscription);
                    if (handler == null) continue;
                    var eventType = _eventTypes.SingleOrDefault(t => t.Name == eventname);
                    var @event = JsonConvert.DeserializeObject(message, eventType);
                    var concretetype = typeof(IEventHandler<>).MakeGenericType(eventType);
                    await (Task)concretetype.GetMethod("Handle").Invoke(handler, new object[] { @event });
                }
            }
        }
    }
}
