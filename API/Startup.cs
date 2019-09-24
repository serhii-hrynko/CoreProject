﻿using System.Net;
using API.Infrastructure;
using API.Infrastructure.Email;
using API.Jobs;
using DAO.Contexts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;

namespace API
{
    public class Startup
    {
        private IServiceCollection _services;
        private readonly IConfiguration _configuration;


        public Startup(IHostingEnvironment env, IConfiguration configuration)
        {
            _configuration = configuration;
        }


        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            _services = services;

            services.AddCors();

            // Contexts
            ConfigureServicesDbContexts();

            // Jobs
            ConfigureServicesScheduledJobs();

            // Uptime Service
            services.AddSingleton(new UptimeService());

            // Email Service
            services.Configure<EmailConfig>(_configuration.GetSection("Email"));
            services.AddTransient<IEmailService, EmailService>();

            services.AddMvc()
                .AddJsonOptions(opts =>
                {
                    opts.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                    //opts.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                    //opts.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
                });
        }


        private void ConfigureServicesScheduledJobs()
        {
            _services.AddSingleton<IJobFactory, JobFactory>();
            _services.AddSingleton<ISchedulerFactory, StdSchedulerFactory>();

            //_services.AddSingleton<>();
            //_services.AddSingleton(new JobSchedule(
            //    jobType: typeof(),
            //    cronExpression: _configuration["Jobs:<taskname>:Schedule"])
            //);

            _services.AddHostedService<QuartzHostedService>();
        }


        private void ConfigureServicesDbContexts()
        {
            string connectionString = _configuration["ConnectionStrings:DefaultConnection"];

            _services.AddDbContext<DbContextMain>(options => options.UseMySQL(connectionString));
        }


        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                //app.UseDeveloperExceptionPage();
                app.UseStatusCodePages();
            }
            else
            {
                ConfigureExceptions(app);
            }

            app.UseCors(builder => builder.AllowAnyOrigin());

            app.UseMvc();
        }


        private void ConfigureExceptions(IApplicationBuilder app)
        {
            app.UseExceptionHandler(options => options.Run(async context =>
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";

                var exception = context.Features.Get<IExceptionHandlerFeature>();

                if (exception != null)
                {
                    var error = JsonConvert.SerializeObject(new
                    {
                        //Message = exception.Error.Message,
                        //Message = 
                        //    $"{exception.Error.Message}\r\n" +
                        //    $"{exception.Error.InnerException?.Message}\r\n" +
                        //    $"{exception.Error.InnerException?.InnerException?.Message}",
                        Message = "Internal Server Error."
                    });;

                    await context.Response.WriteAsync(error);
                }
            }));
        }
    }
}
