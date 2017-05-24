using Microsoft.Owin;
using Owin;
using Hangfire;
using System;
using System.Configuration;
using VersionadorPlSql.Services;

[assembly: OwinStartupAttribute(typeof(VersionadorPlSql.Startup))]
namespace VersionadorPlSql
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            GlobalConfiguration.Configuration.UseSqlServerStorage("DefaultConnection");

            app.UseHangfireDashboard();
            app.UseHangfireServer();


            RecurringJob.AddOrUpdate(() => SourceControlService.Start(), ConfigurationManager.AppSettings["CRON_INTERVAL"]);
        }
    }
}
