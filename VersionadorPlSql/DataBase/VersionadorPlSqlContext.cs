using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using VersionadorPlSql.Models;

namespace VersionadorPlSql.DataBase
{
    public class VersionadorPlSqlContext : DbContext
    {
        public VersionadorPlSqlContext() : base("DefaultConnection")
        {

        }
        public DbSet<User> Users { get; set; }
        public DbSet<FileSourceControl> FilesSourceControl { get; set; }
    }
}