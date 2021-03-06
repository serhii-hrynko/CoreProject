﻿using AutoMapper;
using System;
using System.Net;
using System.Text;
using API.Infrastructure;
using API.Infrastructure.Email;
using API.Infrastructure.Jwt;
using API.Infrastructure.Scheduler;
using DAO.Contexts;
using Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;
using API.Infrastructure.Automapper;
using Microsoft.OpenApi.Models;
using API.Infrastructure.Identity;
using API.Models;
using API.Middlewares;
using API.Infrastructure.MemoryCache;

namespace API
{
    public class Startup
    {
        private IServiceCollection _services;
        private readonly IConfiguration _configuration;


        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }


        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            _services = services;

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "ApsNetCoreProject", Version = "v1" });
            });

            services.AddAutoMapper(typeof(AutoMapperProfile));

            services.AddCors();
            services.AddMemoryCache();

            services.AddSingleton(new UptimeService());
            services.AddSingleton<IUserRolesCachingService, UserRolesCachingService>();

            services.Configure<EmailConfig>(_configuration.GetSection("Email"));
            services.AddTransient<IEmailService, EmailService>();

            services
                .AddMvcCore(options => options.EnableEndpointRouting = false)
                .AddNewtonsoftJson()
                .AddApiExplorer()
                .AddAuthorization()
                .AddFormatterMappings();

            ConfigureServicesJwtAuthentication();

            ConfigureServicesIdentity();

            ConfigureServicesDbContexts();

            ConfigureServicesScheduledJobs();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var isDevelopment = env.IsDevelopment();

            ConfigureExceptions(app, isDevelopment);

            if (!isDevelopment)
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "ApsNetCoreProject v1");
                options.RoutePrefix = string.Empty;
            });

            app.UseCors(builder => builder
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod()
            );

            app.UseAuthentication();

            app.UseRoleProvider();

            app.UseMvc();

            IdentityInitializer.Initialize(
                app.ApplicationServices.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider,
                _configuration.GetSection("Identity:User").Get<CreateUserModel>()
            ).GetAwaiter().GetResult();
        }


        private void ConfigureServicesJwtAuthentication()
        {
            var tokenConfigurationSection = _configuration.GetSection("JwtOptions");
            var tokenOptions = tokenConfigurationSection.Get<JwtOptions>();

            _services.Configure<JwtOptions>(tokenConfigurationSection);
            _services.AddSingleton<IJwtFactory, JwtFactory>();

            var securityKey = Encoding.UTF8.GetBytes(tokenOptions.SecurityKey);

            var tokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(securityKey),
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                ValidateIssuer = false,
                ValidateAudience = false,
            };

            _services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.SaveToken = true;
                    options.TokenValidationParameters = tokenValidationParameters;
                });
        }

        private void ConfigureServicesIdentity()
        {
            _services
                .AddIdentityCore<User>(options =>
                {
                    // Configure Identity password options
                    options.Password.RequireDigit = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireNonAlphanumeric = true;
                    options.Password.RequiredLength = 8;
                    options.User.RequireUniqueEmail = true;
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<DbContextIdentity>()
                .AddDefaultTokenProviders();
        }

        private void ConfigureServicesDbContexts()
        {
            string connectionString = _configuration["ConnectionStrings:DefaultConnection"];

            _services.AddDbContext<DbContextIdentity>(options => options.UseSqlServer(connectionString));
            _services.AddDbContext<DbContextMain>(options => options.UseSqlServer(connectionString));
        }

        private void ConfigureServicesScheduledJobs()
        {
            _services.AddSingleton<IJobFactory, JobFactory>();
            _services.AddSingleton<ISchedulerFactory, StdSchedulerFactory>();

            //_services.AddSingleton<job_class>();
            //_services.AddSingleton(new JobSchedule(
            //    jobType: typeof(job_class),
            //    cronExpression: _configuration["Jobs:<job_name>:Schedule"])
            //);

            _services.AddHostedService<QuartzHostedService>();
        }

        private void ConfigureExceptions(IApplicationBuilder app, bool isDevelopment)
        {
            app.UseExceptionHandler(options => options.Run(async context =>
            {
                const int statusCode = (int)HttpStatusCode.InternalServerError;

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";

                var exception = context.Features.Get<IExceptionHandlerFeature>();

                if (exception != null)
                {
                    var error = CreateErrorDescription(exception, statusCode, isDevelopment);

                    await context.Response.WriteAsync(error);
                }
            }));

            string CreateErrorDescription(IExceptionHandlerFeature ex, int code, bool isDevelopment)
            {
                return JsonConvert.SerializeObject(new
                {
                    code,
                    message = isDevelopment
                        ? (
                            $"{ex.Error.Message}\r\n" +
                            $"{ex.Error.InnerException?.Message}\r\n" +
                            $"{ex.Error.InnerException?.InnerException?.Message}"
                        )
                        : "Internal Server Error",
                    description = !isDevelopment
                        ? ex.Error.ToString()
                        : null
                }); ;
            }
        }
    }
}
