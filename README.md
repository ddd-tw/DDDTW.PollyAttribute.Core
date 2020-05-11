# DDDTW.PollyAttribute.Core
This repo tries to treat Polly to be a .net attribute.

## DDDTW.PollyAttribute.Core

### Provision
There are some differences in .net framework and .net core; If your project is based on .net core, congratulation you! you will feel easy to integrate PollyAttribute into your project.

You just need to install [DDDTW.PollyAttribute.Core nuget package](https://www.nuget.org/packages/DDDTW.PollyAttribute.Core/). After you install package success, then please add a line of code in Program.cs

```csharp
 public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
               .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                .UseServiceProviderFactory(new DynamicProxyServiceProviderFactory());
```

Then you can add PollyAttribute on specific method:

```csharp
public interface IProductService
    {
        string GetProductName();

        string GetProductName_Failback();
    }

    public class ProductService : IProductService
    {
        private static int counter = 0;

        [PollyAsync(FallBackMethod = nameof(GetProductName_Failback), IsEnableCircuitBreaker = true)]
        public string GetProductName()
        {
            if (counter++ >= 3)
                throw new Exception();
            return "Prd";
        }

        public string GetProductName_Failback()
        {
            return "Prd Fall_back";
        }
     }
```

