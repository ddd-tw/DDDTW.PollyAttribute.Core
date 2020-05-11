using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using AspectCore.DynamicProxy;
using Microsoft.Extensions.Caching.Memory;
using Polly;

namespace DDDTW.PollyAttribute.Core
{
    [AttributeUsage(AttributeTargets.Method)]
    public class PollyAsyncAttribute : AbstractInterceptorAttribute
    {
        #region Fields

        private static readonly ConcurrentDictionary<MethodInfo, AsyncPolicy> policies
            = new ConcurrentDictionary<MethodInfo, AsyncPolicy>();

        private static readonly IMemoryCache memoryCache
            = new MemoryCache(new MemoryCacheOptions());

        #endregion Fields

        #region Properties

        public int MaxRetryTimes { get; set; } = 0;
        public int RetryIntervalMilliseconds { get; set; } = 100;
        public bool IsEnableCircuitBreaker { get; set; } = false;
        public int ExceptionsAllowedBeforeBreaking { get; set; } = 3;
        public int MillisecondsOfBreak { get; set; } = 1000;
        public int TimeOutMilliseconds { get; set; } = 0;
        public int CacheTTLMilliseconds { get; set; } = 0;
        public string FallBackMethod { get; set; }

        #endregion Properties

        #region Public Methods

        public override async Task Invoke(AspectContext context, AspectDelegate next)
        {
            policies.TryGetValue(context.ServiceMethod, out var policy);
            lock (policies)
            {
                policy = BuildPolicy(context, policy);
            }

            var pollyCtx = new Context();
            pollyCtx["aspectContext"] = context;

            if (CacheTTLMilliseconds > 0)
            {
                await CachedExecute(context, next, policy, pollyCtx);
            }
            else
            {
                await policy.ExecuteAsync(ctx => next(context), pollyCtx);
            }
        }

        #endregion Public Methods

        #region Private Methods

        private AsyncPolicy BuildPolicy(AspectContext context, AsyncPolicy policy)
        {
            if (policy == null)
            {
                policy = Policy.NoOpAsync();
                if (IsEnableCircuitBreaker)
                {
                    policy = policy.WrapAsync(Policy.Handle<Exception>().CircuitBreakerAsync(
                        ExceptionsAllowedBeforeBreaking,
                        TimeSpan.FromMilliseconds(MillisecondsOfBreak)));
                }

                if (TimeOutMilliseconds > 0)
                {
                    policy = policy.WrapAsync(Policy.TimeoutAsync(() => TimeSpan.FromMilliseconds(TimeOutMilliseconds),
                        global::Polly.Timeout.TimeoutStrategy.Pessimistic));
                }

                if (MaxRetryTimes > 0)
                {
                    policy = policy.WrapAsync(Policy.Handle<Exception>().WaitAndRetryAsync(MaxRetryTimes,
                        i => TimeSpan.FromMilliseconds(RetryIntervalMilliseconds)));
                }

                var policyFallBack = Policy
                    .Handle<Exception>()
                    .FallbackAsync(async (ctx, t) =>
                    {
                        var aspectContext = (AspectContext)ctx["aspectContext"];
                        var fallBackMethod = context.ServiceMethod.DeclaringType.GetMethod(this.FallBackMethod);
                        var fallBackResult = fallBackMethod.Invoke(context.Implementation, context.Parameters);
                        aspectContext.ReturnValue = fallBackResult;
                    }, async (ex, t) => { });

                policy = policyFallBack.WrapAsync(policy);
                policies.TryAdd(context.ServiceMethod, policy);
            }

            return policy;
        }

        private static string GetCacheKey(AspectContext context)
        {
            return "PollyAsyncMethodCacheManager_Key_" + context.ServiceMethod.DeclaringType
                                                       + "." + context.ServiceMethod +
                                                       string.Join("_", context.Parameters);
        }

        private async Task CachedExecute(AspectContext context, AspectDelegate next, AsyncPolicy policy,
            Context pollyCtx)
        {
            var cacheKey = GetCacheKey(context);
            if (memoryCache.TryGetValue(cacheKey, out var cacheValue))
            {
                context.ReturnValue = cacheValue;
            }
            else
            {
                await policy.ExecuteAsync(ctx => next(context), pollyCtx);
                using (var cacheEntry = memoryCache.CreateEntry(cacheKey))
                {
                    cacheEntry.Value = context.ReturnValue;
                    cacheEntry.AbsoluteExpiration = DateTime.Now + TimeSpan.FromMilliseconds(CacheTTLMilliseconds);
                }
            }
        }

        #endregion Private Methods
    }
}