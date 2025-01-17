﻿using System;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using TASVideos.Core.Settings;

namespace TASVideos.Core.Services.Cache
{
	public class RedisCacheService : ICacheService
	{
		private readonly ILogger<RedisCacheService> _logger;
		private static IDatabase _cache = null!;
		private static Lazy<ConnectionMultiplexer>? _connection;
		private readonly int _cacheDurationInSeconds;
		private static readonly JsonSerializerSettings SerializerSettings = new ()
		{
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore
		};

		public RedisCacheService(AppSettings settings, ILogger<RedisCacheService> logger)
		{
			if (string.IsNullOrWhiteSpace(settings.CacheSettings.ConnectionString))
			{
				throw new ArgumentException("Redis connection not configured");
			}

			_logger = logger;
			_cacheDurationInSeconds = settings.CacheSettings.CacheDurationInSeconds;
			_connection ??= new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(settings.CacheSettings.ConnectionString));
			var redis = _connection.Value;
			_cache = redis.GetDatabase();
		}

		public bool TryGetValue<T>(string key, out T value)
		{
			try
			{
				RedisValue data = _cache.StringGet(key);
				if (data.IsNullOrEmpty)
				{
					value = default!;
					return false;
				}

				value = JsonConvert.DeserializeObject<T>(data) ?? default!;
				return true;
			}
			catch (Exception ex)
			{
				if (ex is RedisConnectionException or RedisTimeoutException)
				{
					_logger.LogWarning($"Redis failure on key {key}, silently falling back to uncached");
					value = default!;
					return false;
				}

				throw;
			}
		}

		public void Set(string key, object? data, int? cacheTime = null)
		{
			var serializedData = JsonConvert.SerializeObject(data, SerializerSettings);
			var timeout = TimeSpan.FromSeconds(cacheTime ?? _cacheDurationInSeconds);
			_cache.StringSet(key, serializedData, timeout);
		}

		public void Remove(string key)
		{
			_cache.KeyDelete(key);
		}
	}
}
