namespace VersionadorPlSql.DataBase
{
    using System;

    public class FileSourceControl
    {
        public int FileSourceControlId { get; set; }

        public string Name { get; set; }

        public DateTime FileCreated { get; set; }

        public DateTime FileLastModify { get; set; }

        public DateTime CodeCreated { get; set; }

        public bool IsCheckIn { get; set; }
    }
}