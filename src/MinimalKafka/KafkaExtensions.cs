﻿using Confluent.Kafka;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MinimalKafka.Builders;
using MinimalKafka.Extension;
using MinimalKafka.Serializers;
using System.Text.Json;

namespace MinimalKafka;

public static class KafkaExtensions
{
    public static IServiceCollection AddMinimalKafka(this IServiceCollection services, Action<IKafkaConventionBuilder> config)
    {
        var conventions = new List<Action<IKafkaBuilder>>();
        var configBuilder = new KafkaConventionBuilder(conventions, []);

        configBuilder.WithKeyDeserializer(typeof(JsonTextSerializer<>));
        configBuilder.WithValueDeserializer(typeof(JsonTextSerializer<>));

        config(configBuilder);

        services.TryAddSingleton(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        services.AddTransient(typeof(JsonTextSerializer<>));

        services.AddSingleton<IKafkaBuilder>(s =>
        {
            var b = new KafkaBuilder(s);
            conventions.ForEach(x => x(b));
            return b;
        });
        services.AddHostedService<KafkaService>();

        return services;
    }

    public static IKafkaConventionBuilder MapTopic(this IApplicationBuilder builder, string topic, Delegate handler)
    {
        var tb = builder.ApplicationServices.GetRequiredService<IKafkaBuilder>();
        return tb.MapTopic(topic, handler);
    }
    public static IKafkaConventionBuilder MapTopic(this IKafkaBuilder builder, string topic, Delegate handler)
    {
        return builder
            .GetOrAddTopicDataSource()
            .AddTopicDelegate(topic, handler)
            .WithMetaData([.. builder.MetaData]);
    }
    public static TBuilder WithMetaData<TBuilder>(this TBuilder builder, params object[] items)
        where TBuilder : IKafkaConventionBuilder
    {
        builder.Add(b =>
        {
            foreach (var item in items)
            {
                b.MetaData.Add(item);
            }
        });

        return builder;
    }
    public static TBuilder WithSingle<TBuilder>(this TBuilder builder, object metadata)
        where TBuilder : IKafkaConventionBuilder
    {
        builder.RemoveMetaData(metadata);
        builder.WithMetaData(metadata);
        return builder;
    }
    public static TBuilder RemoveMetaData<TBuilder>(this TBuilder builder, object item)
        where TBuilder : IKafkaConventionBuilder
    {
        builder.Add(b =>
        {
            b.MetaData.RemoveAll(x => x.GetType() == item.GetType());
        });

        return builder;
    }
    private static IKafkaDataSource GetOrAddTopicDataSource(this IKafkaBuilder builder)
    {
        builder.DataSource ??= new KafkaDataSource(builder.ServiceProvider);
        return builder.DataSource;
    }


    public static Task<DeliveryResult<TKey, TValue>> ProduceAsync<TKey, TValue>(this KafkaContext context, string topic, TKey key, TValue value)
        => Produce(context, topic, new Message<TKey, TValue>() {  Key = key, Value = value });

    public static async Task<DeliveryResult<TKey, TValue>> Produce<TKey, TValue>(this KafkaContext context, string topic, Message<TKey, TValue> message)
    {
        var producer = new KafkaProducerBuilder<TKey, TValue>(context.MetaData, context.RequestServices)
            .Build();
        return await producer.ProduceAsync(topic, message);   
    }
}
