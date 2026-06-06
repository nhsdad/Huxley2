// © James Singleton. EUPL-1.2 (see the LICENSE file for the full license governing this code).
 
using System;
using System.Net.Http;
using Huxley2.Interfaces;
using Huxley2.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenLDBSVWS;
 
namespace Huxley2
{
    public class Startup
    {
        private readonly bool _enableUpdateCheck;
 
        public Startup(IConfiguration config)
        {
            _enableUpdateCheck = config.GetValue<bool>("EnableUpdateCheck");
        }
 
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
            });
            services.AddResponseCaching();
            services.AddControllers();
            services.AddRazorPages();
            services.AddCors();
 
            // Singleton SOAP clients
            services.AddSingleton<OpenLDBWS.LDBServiceSoap, OpenLDBWS.LDBServiceSoapClient>(_ =>
                new OpenLDBWS.LDBServiceSoapClient(OpenLDBWS.LDBServiceSoapClient.EndpointConfiguration.LDBServiceSoap));
            services.AddSingleton<LDBSVServiceSoap, LDBSVServiceSoapClient>(_ =>
                new LDBSVServiceSoapClient(LDBSVServiceSoapClient.EndpointConfiguration.LDBSVServiceSoap));
            services.AddSingleton<LDBSVRefServiceSoap, LDBSVRefServiceSoapClient>(_ =>
                new LDBSVRefServiceSoapClient(LDBSVRefServiceSoapClient.EndpointConfiguration.LDBSVRefServiceSoap));
 
            services.AddSingleton<IAccessTokenService, AccessTokenService>();
            services.AddSingleton<ICrsService, CrsService>();
            services.AddSingleton<IDateTimeService, DateTimeService>();
            services.AddSingleton<IMapperService, MapperService>();
 
            // Singleton HTTP client
            services.AddSingleton<HttpClient>();
 
            // HeadcodeService — fetches trainid from Rail Data Marketplace
            services.AddSingleton<HeadcodeService>();
 
            services.AddSingleton<IStationBoardService, StationBoardService>();
            services.AddSingleton<IStationBoardStaffService, StationBoardStaffService>();
            services.AddSingleton<IDelaysService, DelaysService>();
            services.AddSingleton<IServiceDetailsService, ServiceDetailsService>();
            services.AddSingleton<IUpdateCheckService, UpdateCheckService>();
        }
 
        public async void Configure(
            IApplicationBuilder app,
            IWebHostEnvironment env,
            ILogger<Startup> logger,
            ICrsService crsService,
            IUpdateCheckService updateCheckService)
        {
            logger.LogInformation("Configuring Huxley 2 web API application");
 
            app.UseResponseCompression();
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseETagger();
            app.UseRouting();
            app.UseResponseCaching();
            app.UseCors(config => config.AllowAnyOrigin());
 
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapRazorPages();
            });
 
            logger.LogInformation("Huxley 2 web API application configured");
 
            try
            {
                logger.LogInformation("Loading CRS station codes from remote source");
                await crsService.LoadCrsCodes();
                if (_enableUpdateCheck)
                {
                    logger.LogInformation("Checking for any available updates to Huxley");
                    await updateCheckService.CheckForUpdates();
                }
            }
            catch (Exception e) when (
                e is CrsServiceException ||
                e is UpdateCheckServiceException
                )
            {
                logger.LogError(e, "Non-fatal startup failure");
            }
 
            logger.LogInformation("Huxley 2 web API application ready");
        }
    }
}
